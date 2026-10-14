using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HotbarFont;

/// <summary>
/// 把国服技能栏上的快捷键文字（sR、cQ、º1 等）改成国际服那样的 MiedingerMed 字体 + 大写（SR、CQ）。
///
/// 国服对含非数字字符的快捷键会无视节点的 FontType，改用中文字体；
/// 给节点打开 KeepFontFlag 后才会按 FontType 显示。纯数字的单键游戏本来就显示正常，不处理。
///
/// 事件驱动版：不再每帧检查，只在游戏"建立 / 刷新技能栏"时处理一次。
///   - PostSetup：技能栏窗口刚创建（登录、切换角色）
///   - PostRefresh：技能栏整体刷新（切换职业、切换技能栏页）
///   - PostRequestedUpdate：技能栏内容被要求更新（改键位、拖动技能）
/// 冷却动画不会触发这些事件，所以战斗中开销为零。
/// </summary>
public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
#if DEVTOOLS
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!; // 只有 GlyphProbe 用
#endif

    private const string Command = "/hbfont";

    // 1~10 号普通技能栏 + 宠物技能栏
    private static readonly string[] AddonNames =
    {
        "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04",
        "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09",
        "_ActionBarEx",
    };

    private static readonly AddonEvent[] Events =
    {
        AddonEvent.PostSetup,
        AddonEvent.PostRefresh,
        AddonEvent.PostRequestedUpdate,
    };

    internal static readonly string[] FontNames =
        { "Axis", "MiedingerMed", "Miedinger", "TrumpGothic", "Jupiter", "JupiterLarge" };

    internal static Configuration Config = null!;

    private readonly WindowSystem windowSystem = new("HotbarFont");
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        // 插件列表里的"设置"和"打开"按钮都打开同一个设置窗口
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        foreach (var e in Events)
            AddonLifecycle.RegisterListener(e, AddonNames, OnAddonEvent);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "开/关；/hbfont config 打开设置；/hbfont fix 手动重新应用",
        });

        // 插件加载时技能栏早就建好了，PostSetup 已经错过，所以主动处理一遍。
        // 访问游戏界面必须在游戏主线程上做。
        Framework.RunOnFrameworkThread(ApplyToAllBars);
    }

    public void Dispose()
    {
        foreach (var e in Events)
            AddonLifecycle.UnregisterListener(e, AddonNames, OnAddonEvent);
        CommandManager.RemoveHandler(Command);

        // 卸载时把技能栏恢复原样；访问游戏界面必须在主线程（已在主线程时会直接执行）
        Framework.RunOnFrameworkThread(RestoreAllBars).Wait();

        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        windowSystem.RemoveAllWindows();
    }

    private void ToggleConfigUi() => configWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        args = args.Trim().ToLowerInvariant();

        switch (args)
        {
            case "":
                Config.Enabled = !Config.Enabled;
                Config.Save();
                ApplyToAllBars(); // 关闭时会恢复原样
                Chat.Print($"[HotbarFont] {(Config.Enabled ? "已开启" : "已关闭")}");
                break;
            case "config":
                ToggleConfigUi();
                break;
            case "fix":
                ApplyToAllBars();
                Chat.Print("[HotbarFont] 已手动重新应用");
                break;
            case "debug":
                DebugPrint();
                break;
#if DEVTOOLS
            // 调试命令只在测试版里有，见 DevTools 文件夹和 HotbarFont.csproj 里的 DevTools
            case "actinfo":
                ActProbe.Info();
                break;
            case "actsig":
                ActSigProbe.Run();
                break;
            case "rawflag":
            case "rawflag off":
                ActProbe.RawFlag(args == "rawflag");
                break;
            case "glyphs":
                GlyphProbe.List();
                break;
            case var s when s.StartsWith("preview ") && int.TryParse(s[8..], out var page):
                GlyphProbe.Preview(page);
                break;
            case var s when s.StartsWith("show "):
                GlyphProbe.Show(s[5..]);
                break;
#endif
            default:
                Chat.Print("[HotbarFont] 用法：/hbfont（开关） | /hbfont config（设置） | /hbfont fix（重新应用）");
                break;
        }
    }

    // 游戏刷新某条技能栏后调用
    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        ProcessAddon((AtkUnitBase*)(nint)args.Addon);
    }

    // 主动处理所有技能栏（加载插件、改设置、手动 fix 时）
    internal void ApplyToAllBars() => ForEachBar(ProcessAddon);

    private void ProcessAddon(AtkUnitBase* addon)
    {
        if (addon == null) return;

        if (!Config.Enabled)
            ForEachHotkeyNode(addon, RestoreTextNode);
        else if (Config.IsFontTypeValid())
            ForEachHotkeyNode(addon, FixTextNode);
    }

    // 卸载插件时把所有技能栏恢复原样
    private void RestoreAllBars()
    {
        ForEachBar(addon => ForEachHotkeyNode(addon, RestoreTextNode));
        NodeStates.Clear();
    }

    private delegate void AddonVisitor(AtkUnitBase* addon);
    internal delegate void TextNodeVisitor(AtkTextNode* textNode);

    // 对当前存在的每条技能栏执行 visit（没打开的技能栏跳过）
    private static void ForEachBar(AddonVisitor visit)
    {
        foreach (var name in AddonNames)
        {
            var addon = (AtkUnitBase*)(nint)GameGui.GetAddonByName(name, 1);
            if (addon != null)
                visit(addon);
        }
    }

    // 技能格子组件里，快捷键文字节点的 NodeId 是 2（/hbfont debug 实测）。
    // 技能消耗、充能层数、冷却秒数是别的节点，靠这个排除。
    internal const uint HotkeyNodeId = 2;

    // 遍历一条技能栏的所有快捷键文字节点（含纯数字的单键）。
    // 结构是 技能栏 → 每个技能格子组件 → 组件里 NodeId 2 的文字节点，只有一层组件（/hbfont actinfo 实测）。
    internal static void ForEachHotkeyNode(AtkUnitBase* addon, TextNodeVisitor visit)
    {
        var uld = &addon->UldManager;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (ushort)node->Type < 1000) continue; // 只看组件节点

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null) continue;

            var slot = &component->UldManager;
            for (var j = 0; j < slot->NodeListCount; j++)
            {
                var child = slot->NodeList[j];
                if (child != null && child->Type == NodeType.Text && child->NodeId == HotkeyNodeId)
                    visit((AtkTextNode*)child);
            }
        }
    }

    // 需要处理的快捷键文字：不是纯数字。纯数字的单键（1、2、3）游戏本来就用 MiedingerMed 正常显示，不用碰；
    // 其余的（sR、cQ、小键盘的 º1 等）在国服会被换成中文字体。
    private static bool IsHotkeyText(string text) =>
        !string.IsNullOrEmpty(text) && text.Length <= 6 && !text.All(char.IsAsciiDigit);

    // 国服对含非数字字符的文字会无视 FontType，改用中文字体显示；给节点加上这一位后才会按 FontType 显示。
    // 实测得出（逐位翻转 TextFlags 测试），游戏里这一位的正式含义未知。
    private const TextFlags KeepFontFlag = (TextFlags)0x2000;

    // 游戏给技能栏快捷键节点设的原始字体（/hbfont debug 实测），恢复时用它
    internal const FontType OriginalFont = FontType.MiedingerMed;

    /// <summary>被插件改过的节点的原文字，用于关闭插件 / 取消大写时恢复。字体统一恢复为 OriginalFont，不用记。</summary>
    private sealed class NodeState
    {
        public required string Original;      // 游戏写入的原文字，如 sR
        public required string Written;       // 插件最后写入的文字，如 SR
    }

    // 以节点地址为键。只在遍历到"活着的"节点时才按地址查表，所以技能栏销毁后残留的旧条目不会被误用。
    private static readonly Dictionary<nint, NodeState> NodeStates = new();

    private static void FixTextNode(AtkTextNode* textNode)
    {
        var text = textNode->NodeText.ToString();

        if (NodeStates.TryGetValue((nint)textNode, out var state))
        {
            // 文字和插件写的不一样 = 游戏改过（改键位、换技能），以游戏的新文字为原文
            if (text != state.Written)
                state.Original = text;
        }
        else
        {
            if (!IsHotkeyText(text)) return;
            state = new NodeState { Original = text, Written = text };
            NodeStates[(nint)textNode] = state;
        }

        // 游戏把快捷键清掉了（变成空或纯数字）：恢复原样，不再接管这个节点
        if (!IsHotkeyText(state.Original))
        {
            SetLook(textNode, OriginalFont, false, text);
            NodeStates.Remove((nint)textNode);
            return;
        }

        var font = (FontType)Config.FontType;
        var newText = BuildText(state.Original, font);
        state.Written = newText;

        SetLook(textNode, font, true, newText);
    }

    // 国服快捷键开头的修饰键是小写字母，顺序固定为 c(Ctrl) → a(Alt) → s(Shift)，如 cs1、cas1、sR。
    // 国服 MiedingerMed 字体里有国际服用的修饰键符号，借用了 Latin-1 区的字符位（实测结果见 docs/modifier-symbols.md）。
    private static readonly Dictionary<string, string> ModifierSymbolMap = new()
    {
        ["c"] = "¢",   // 小号 C
        ["a"] = "ª",   // 小号 A
        ["s"] = "§",   // 上箭头
        ["ca"] = "¾",  // 小号 C 叠在小号 A 上（尚未和国际服核实）
        ["cs"] = "½",  // 小号 C + 上箭头
        ["as"] = "¼",  // 小号 A + 上箭头
        ["cas"] = "¶", // 三条横线（已和国际服核实）
    };

    // 根据设置算出要显示的文字。开启修饰键符号时：MiedingerMed 用符号，其他字体用大写 C / A / S；
    // "转为大写"只作用于按键部分。未开启时沿用原逻辑，整串按"转为大写"处理。
    private static string BuildText(string original, FontType font)
    {
        var modifiers = ModifierPrefixLength(original);
        if (!Config.ModifierSymbols || modifiers == 0)
            return Config.Uppercase ? original.ToUpperInvariant() : original;

        var prefix = original[..modifiers];
        var key = original[modifiers..];
        var symbol = font == FontType.MiedingerMed && ModifierSymbolMap.TryGetValue(prefix, out var s)
            ? s
            : prefix.ToUpperInvariant();
        return symbol + (Config.Uppercase ? key.ToUpperInvariant() : key);
    }

    // 开头按 c、a、s 顺序出现的小写修饰键有几个；后面必须还剩按键，否则不算修饰键
    private static int ModifierPrefixLength(string text)
    {
        var i = 0;
        foreach (var m in "cas")
        {
            if (i < text.Length && text[i] == m)
                i++;
        }
        return i < text.Length ? i : 0;
    }

    private static void RestoreTextNode(AtkTextNode* textNode)
    {
        if (!NodeStates.Remove((nint)textNode, out var state)) return;

        // 文字已被游戏改过就不覆盖，只恢复字体
        var text = textNode->NodeText.ToString();
        SetLook(textNode, OriginalFont, false, text == state.Written ? state.Original : text);
    }

    // 设置字体、KeepFontFlag 和文字，只改和目标不一样的部分。
    // 实测改标志位、改文字后游戏会立即按新设置显示，不需要先清空文字强制重新排版。
    // 游戏开启"隐藏空栏位"时是把快捷键节点设为不可见，而 SetText 会把节点重新设为可见，所以要保存并恢复可见性。
    internal static void SetLook(AtkTextNode* textNode, FontType font, bool keepFont, string text)
    {
        if (textNode->FontType != font)
            textNode->SetFont(font);

        if (keepFont)
            textNode->TextFlags |= KeepFontFlag;
        else
            textNode->TextFlags &= ~KeepFontFlag;

        if (textNode->NodeText.ToString() != text)
        {
            var visible = textNode->AtkResNode.IsVisible();
            textNode->SetText(text);
            textNode->AtkResNode.ToggleVisibility(visible);
        }
    }

    // /hbfont debug：打印一号技能栏里快捷键节点（NodeId 2，含单独数字键）的字体、标志位和可见性，排查用
    private void DebugPrint()
    {
        var addon = (AtkUnitBase*)(nint)GameGui.GetAddonByName("_ActionBar", 1);
        if (addon == null)
        {
            Chat.Print("[HotbarFont] 找不到 _ActionBar");
            return;
        }

        ForEachHotkeyNode(addon, t =>
        {
            var text = t->NodeText.ToString();
            if (string.IsNullOrEmpty(text) || text.Length > 6) return;
            // 附上每个字符的编码，能看出中文、特殊图标字符等
            var codes = string.Join(" ", text.Select(c => ((int)c).ToString("X4")));
            Chat.Print($"[HotbarFont] \"{text}\" [{codes}] FontType={(int)t->FontType} FontSize={t->FontSize} TextFlags={(int)t->TextFlags} 可见={t->AtkResNode.IsVisible()}");
        });
    }
}
