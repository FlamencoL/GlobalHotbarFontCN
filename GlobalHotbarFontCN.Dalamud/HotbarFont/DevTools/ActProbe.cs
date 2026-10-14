using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HotbarFont;

/// <summary>
/// 调试：为 ACT 版收集信息。ACT 插件在游戏进程外，只能跨进程读写内存，拿不到 ClientStructs，
/// 所以先在这里用 ClientStructs 量出真实的结构偏移，再写进 ACT 版。
///
/// /hbfont actinfo：打印 ACT 版需要的各项偏移和从技能栏到快捷键节点的路径。
/// /hbfont rawflag [off]：模拟 ACT 版能做的事——只往一号技能栏快捷键节点的 TextFlags 写 0x2000，
///   不调用 SetFont / SetText，看字体会不会立即生效。测试前先用 /hbfont 关闭插件功能，免得插件自己的处理干扰结果。
/// </summary>
internal static unsafe class ActProbe
{
    private const TextFlags KeepFontFlag = (TextFlags)0x2000;

    public static void Info()
    {
        var addon = (AtkUnitBase*)(nint)Plugin.GameGui.GetAddonByName("_ActionBar", 1);
        if (addon == null)
        {
            Print("找不到 _ActionBar");
            return;
        }

        var module = Process.GetCurrentProcess().MainModule!;
        var moduleBase = module.BaseAddress;
        var vtable = *(nint*)addon;
        Print($"模块 {module.ModuleName} 基址 0x{moduleBase:X} 大小 0x{module.ModuleMemorySize:X}");
        Print($"_ActionBar 0x{(nint)addon:X}，虚表 0x{vtable:X}（模块偏移 0x{vtable - moduleBase:X}）");
        Print($"AtkUnitBase +0x8 处字符串：{Marshal.PtrToStringAnsi((nint)addon + 8)}");
        Print($"AtkUnitBase.UldManager = +0x{Off(addon, &addon->UldManager):X}");

        var uld = &addon->UldManager;
        Print($"AtkUldManager.NodeList = +0x{Off(uld, &uld->NodeList):X}，NodeListCount = +0x{Off(uld, &uld->NodeListCount):X}（{sizeof(ushort)} 字节）");

        // 找快捷键节点，同时记录从技能栏一路下来的路径
        if (!FindHotkey(uld, 0, "_ActionBar", out var text, out var path, out var component, out var componentNode))
        {
            Print("没有找到带字母的快捷键节点（NodeId 2）");
            return;
        }

        var res = &text->AtkResNode;
        Print($"路径：{path}");
        Print($"AtkResNode.NodeId = +0x{Off(res, &res->NodeId):X}，Type = +0x{Off(res, &res->Type):X}（{sizeof(NodeType)} 字节）");
        Print($"AtkComponentNode.Component = +0x{Off(componentNode, &componentNode->Component):X}");
        Print($"AtkComponentBase.UldManager = +0x{Off(component, &component->UldManager):X}");
        Print($"AtkTextNode.TextFlags = +0x{Off(text, &text->TextFlags):X}（{sizeof(TextFlags)} 字节）");
        // StringPtr 在 ClientStructs 里是属性，取不了地址；按 Utf8String 的布局它在开头，直接读开头 8 字节验证
        var stringPtr = *(nint*)&text->NodeText;
        Print($"AtkTextNode.NodeText = +0x{Off(text, &text->NodeText):X}，其 +0x0 处指针 0x{stringPtr:X} → \"{Marshal.PtrToStringUTF8(stringPtr)}\"");
        Print($"示例节点 0x{(nint)text:X}：文字 \"{text->NodeText}\"，TextFlags=0x{(int)text->TextFlags:X}，FontType={(int)text->FontType}");
    }

    public static void RawFlag(bool on)
    {
        var addon = (AtkUnitBase*)(nint)Plugin.GameGui.GetAddonByName("_ActionBar", 1);
        if (addon == null)
        {
            Print("找不到 _ActionBar");
            return;
        }

        var count = 0;
        Plugin.ForEachHotkeyNode(addon, t =>
        {
            var s = t->NodeText.ToString();
            if (string.IsNullOrEmpty(s) || s.All(char.IsAsciiDigit)) return;

            // 只改字段，不调用任何游戏函数——ACT 版跨进程写内存只能做到这一步
            if (on)
                t->TextFlags |= KeepFontFlag;
            else
                t->TextFlags &= ~KeepFontFlag;
            count++;
        });
        Print($"已直接{(on ? "打开" : "关闭")} {count} 个快捷键的 0x2000（未重新排版）。看字体有没有立即变化；" +
              "没变的话再切换技能栏页，看游戏重写文字后会不会变");
    }

    // 递归找第一个 NodeId 2、含非数字字符的文字节点，记下路径和它所在的组件
    private static bool FindHotkey(AtkUldManager* uld, int depth, string path,
        out AtkTextNode* text, out string foundPath, out AtkComponentBase* component, out AtkComponentNode* componentNode)
    {
        text = null;
        foundPath = path;
        component = null;
        componentNode = null;
        if (uld == null || depth > 6) return false;

        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;

            if ((ushort)node->Type >= 1000)
            {
                var cn = (AtkComponentNode*)node;
                if (cn->Component == null) continue;
                var sub = $"{path} → NodeList[{i}] 组件节点 #{node->NodeId}（类型 {(ushort)node->Type}）";
                if (FindHotkey(&cn->Component->UldManager, depth + 1, sub, out text, out foundPath, out component, out componentNode))
                {
                    if (component == null)
                    {
                        component = cn->Component;
                        componentNode = cn;
                    }
                    return true;
                }
            }
            else if (node->Type == NodeType.Text && node->NodeId == Plugin.HotkeyNodeId)
            {
                var t = (AtkTextNode*)node;
                var s = t->NodeText.ToString();
                if (string.IsNullOrEmpty(s) || s.All(char.IsAsciiDigit)) continue;
                text = t;
                foundPath = $"{path} → NodeList[{i}] 文字节点 #{node->NodeId}（深度 {depth}）";
                return true;
            }
        }
        return false;
    }

    private static long Off(void* baseAddress, void* field) => (byte*)field - (byte*)baseAddress;

    private static void Print(string message) => Plugin.Chat.Print($"[HotbarFont] {message}");
}
