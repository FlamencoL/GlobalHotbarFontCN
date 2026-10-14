using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GlobalHotbarFontCN_ACT;

/// <summary>
/// 给技能栏快捷键节点打开 TextFlags 的 0x2000，让国服按节点设置的 MiedingerMed 字体显示，而不是换成中文黑体。
///
/// 不调用游戏函数（跨进程做不到），只直接改内存：标志位实测写入后字体立即生效。
/// 实验功能"修饰键符号"直接改写快捷键文字的 Utf8String（如 cs1 → ½1），写入前核对布局和缓冲区容量。
/// 特征码和偏移为国服实测值，游戏大版本更新后可能需要重新测量。
/// </summary>
internal sealed class HotbarPatcher
{
    // ---- 定位 AtkStage 的特征码：mov rax, [rip+disp32]，disp32 指向存放 AtkStage 指针的静态变量 ----
    private static readonly string[] StageSignatures =
    {
        "48 8B 05 ?? ?? ?? ?? 4C 8B 40 18 45 8B 40 18",
        "48 8B 05 ?? ?? ?? ?? 48 8B D9 8B FA 48 8B 48 18",
    };

    // ---- 结构偏移 ----
    private const int StageUnitManager = 0x20;       // AtkStage.RaptureAtkUnitManager
    private const int UnitManagerAllLoaded = 0x6900; // AtkUnitManager.AllLoadedUnitsList
    private const int UnitListEntries = 0x8;         // AtkUnitList 元素数组（AtkUnitBase* × 256）
    private const int UnitListCount = 0x808;         // AtkUnitList.Count（u16）
    private const int UnitBaseName = 0x8;            // AtkUnitBase.Name（char[32]）
    private const int UnitBaseUldManager = 0x28;     // AtkUnitBase.UldManager
    private const int UldNodeList = 0x50;            // AtkUldManager.NodeList（AtkResNode**）
    private const int UldNodeListCount = 0x42;       // AtkUldManager.NodeListCount（u16）
    private const int NodeId = 0x8;                  // AtkResNode.NodeId（u32）
    private const int NodeType = 0x40;               // AtkResNode.Type（u16）
    private const int ComponentNodeComponent = 0xC0; // AtkComponentNode.Component
    private const int ComponentUldManager = 0x8;     // AtkComponentBase.UldManager
    private const int TextNodeText = 0xD0;           // AtkTextNode.NodeText（Utf8String，开头是字符串指针）
    private const int TextNodeFlagsHighByte = 0x171; // AtkTextNode.TextFlags（u16，+0x170）的高字节

    private const int TextNodeType = 3;
    private const int HotkeyNodeId = 2;              // 技能格子组件里快捷键文字节点的 NodeId
    private const byte KeepFontBit = 0x20;           // TextFlags 0x2000 落在高字节的 0x20

    private static readonly HashSet<string> HotbarNames = new()
    {
        "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04",
        "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09",
        "_ActionBarEx",
    };

    private readonly GameMemory memory;
    private readonly long stageSlot;

    private HotbarPatcher(GameMemory memory, long stageSlot)
    {
        this.memory = memory;
        this.stageSlot = stageSlot;
    }

    /// <summary>用特征码找到存放 AtkStage 指针的静态变量。各特征码的结果必须一致，否则视为失败。</summary>
    public static HotbarPatcher Create(GameMemory memory, out string message)
    {
        var slots = new HashSet<long>();
        foreach (var signature in StageSignatures)
        {
            foreach (var hit in memory.ScanModule(signature))
            {
                var disp = BitConverter.ToInt32(memory.ReadBytes(hit + 3, 4) ?? new byte[4], 0);
                slots.Add(hit + 7 + disp);
            }
        }

        if (slots.Count != 1)
        {
            message = slots.Count == 0
                ? "特征码没有匹配，游戏更新后可能需要更新插件"
                : $"特征码匹配到 {slots.Count} 个不同位置，无法确定，游戏更新后可能需要更新插件";
            return null;
        }

        var slot = slots.First();
        message = $"已定位界面管理器（模块偏移 0x{slot - memory.ModuleBase:X}）";
        return new HotbarPatcher(memory, slot);
    }

    /// <summary>
    /// 对所有已加载的技能栏执行一遍。font：打开 / 关闭字体标志位；symbols：修饰键换成符号 / 换回字母。
    /// 返回改动的节点数，未登录时返回 -1；skipped 为因文字布局不符而跳过的节点数。
    /// </summary>
    public int Run(bool font, bool symbols, out int skipped)
    {
        skipped = 0;
        var stage = memory.ReadPointer(stageSlot);
        var manager = memory.ReadPointer(stage + StageUnitManager);
        if (!GameMemory.IsPlausibleAddress(stage) || !GameMemory.IsPlausibleAddress(manager)) return -1;

        var list = manager + UnitManagerAllLoaded;
        var count = memory.ReadUInt16(list + UnitListCount);
        if (count <= 0 || count > 256) return -1;

        var entries = memory.ReadBytes(list + UnitListEntries, count * 8);
        if (entries == null) return -1;

        var changed = 0;
        for (var i = 0; i < count; i++)
        {
            var addon = BitConverter.ToInt64(entries, i * 8);
            if (!GameMemory.IsPlausibleAddress(addon) || !HotbarNames.Contains(ReadName(addon))) continue;
            changed += PatchAddon(addon, font, symbols, ref skipped);
        }
        return changed;
    }

    // 技能栏界面 → 每个技能格子组件 → 组件里 NodeId 2 的文字节点（只有一层组件，实测）
    private int PatchAddon(long addon, bool font, bool symbols, ref int skipped)
    {
        var changed = 0;
        foreach (var node in ReadNodeList(addon + UnitBaseUldManager))
        {
            var header = memory.ReadBytes(node, NodeType + 2);
            if (header == null || BitConverter.ToUInt16(header, NodeType) < 1000) continue; // 只看组件节点

            var component = memory.ReadPointer(node + ComponentNodeComponent);
            if (!GameMemory.IsPlausibleAddress(component)) continue;

            foreach (var child in ReadNodeList(component + ComponentUldManager))
            {
                var childHeader = memory.ReadBytes(child, NodeType + 2);
                if (childHeader == null
                    || BitConverter.ToUInt32(childHeader, NodeId) != HotkeyNodeId
                    || BitConverter.ToUInt16(childHeader, NodeType) != TextNodeType)
                    continue;

                var text = ReadText(child + TextNodeText);
                if (!IsHotkeyText(text)) continue;

                var flags = memory.ReadByte(child + TextNodeFlagsHighByte);
                if (flags < 0) continue;

                var wanted = font ? flags | KeepFontBit : flags & ~KeepFontBit;
                if (wanted != flags && memory.WriteByte(child + TextNodeFlagsHighByte, (byte)wanted))
                    changed++;

                // 修饰键符号：开启时 cs1 → ½1；关闭时换回字母。游戏刷新技能栏会写回原文，下一次检查再换
                var wantedText = symbols ? ToSymbols(text) : FromSymbols(text);
                if (wantedText == text) continue;
                if (WriteText(child + TextNodeText, text, wantedText))
                    changed++;
                else
                    skipped++;
            }
        }
        return changed;
    }

    // ---- 修饰键符号 ----
    // 国服快捷键开头的修饰键是小写字母，顺序固定为 c(Ctrl) → a(Alt) → s(Shift)，如 cs1、cas1、sR。
    // 国服 MiedingerMed 字体里有国际服用的修饰键符号，借用了 Latin-1 区的字符位（实测）。
    private static readonly Dictionary<string, string> ModifierSymbols = new()
    {
        ["c"] = "¢",   // 小号 C
        ["a"] = "ª",   // 小号 A
        ["s"] = "§",   // 上箭头
        ["ca"] = "¾",  // 小号 C 叠在小号 A 上（尚未和国际服核实）
        ["cs"] = "½",  // 小号 C + 上箭头
        ["as"] = "¼",  // 小号 A + 上箭头
        ["cas"] = "¶", // 三条横线
    };

    private static readonly Dictionary<char, string> SymbolModifiers =
        ModifierSymbols.ToDictionary(p => p.Value[0], p => p.Key);

    private static string ToSymbols(string text)
    {
        var length = ModifierPrefixLength(text);
        return length == 0 ? text : ModifierSymbols[text.Substring(0, length)] + text.Substring(length);
    }

    private static string FromSymbols(string text) =>
        text.Length > 1 && SymbolModifiers.TryGetValue(text[0], out var modifiers) ? modifiers + text.Substring(1) : text;

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

    // ---- 直接改写 Utf8String（不调用游戏函数）----
    // Utf8String 布局：+0x00 文字指针 | +0x08 缓冲区容量 | +0x10 已用字节（含结尾 0）| +0x18 文字长度 | +0x22 内联缓冲区（0x40 字节）
    private const int StrBufSize = 0x08;
    private const int StrBufUsed = 0x10;
    private const int StrLength = 0x18;
    private const int StrInlineBuffer = 0x22;
    private const int StrInlineBufferSize = 0x40;

    /// <summary>最近一次布局不符时的实际字段值（诊断用）</summary>
    public string LastMismatch { get; private set; }

    /// <summary>
    /// 先核对布局（已用字节、长度必须和读到的原文一致），再确认缓冲区放得下，才写入；任何一项不符就不写，返回 false。
    /// </summary>
    private bool WriteText(long utf8String, string oldText, string newText)
    {
        var header = memory.ReadBytes(utf8String, StrInlineBuffer);
        if (header == null) return false;

        var pointer = BitConverter.ToInt64(header, 0);
        var bufSize = BitConverter.ToInt64(header, StrBufSize);
        var bufUsed = BitConverter.ToInt64(header, StrBufUsed);
        var length = BitConverter.ToInt64(header, StrLength);

        var oldBytes = Encoding.UTF8.GetBytes(oldText);
        var newBytes = Encoding.UTF8.GetBytes(newText);
        if (bufUsed != oldBytes.Length + 1 || length != oldBytes.Length)
        {
            // 诊断：记下实际的字段值，显示在插件页签里，用来修正布局
            LastMismatch = $"\"{oldText}\"（{oldBytes.Length} 字节）指针 {(pointer == utf8String + StrInlineBuffer ? "=内联缓冲区" : $"0x{pointer:X}")} " +
                           $"容量 {bufSize} 已用 {bufUsed} 长度 {length} +0x20~0x21 {header[0x20]:X2} {header[0x21]:X2}";
            return false;
        }

        var capacity = pointer == utf8String + StrInlineBuffer ? Math.Min(bufSize, StrInlineBufferSize) : bufSize;
        if (newBytes.Length + 1 > capacity) return false;

        // 先写内容（带结尾 0），再写长度
        var content = new byte[newBytes.Length + 1];
        Array.Copy(newBytes, content, newBytes.Length);
        return memory.WriteBytes(pointer, content)
            && memory.WriteBytes(utf8String + StrBufUsed, BitConverter.GetBytes((long)content.Length))
            && memory.WriteBytes(utf8String + StrLength, BitConverter.GetBytes((long)newBytes.Length));
    }

    private IEnumerable<long> ReadNodeList(long uldManager)
    {
        var count = memory.ReadUInt16(uldManager + UldNodeListCount);
        var array = memory.ReadPointer(uldManager + UldNodeList);
        if (count <= 0 || count > 1000 || !GameMemory.IsPlausibleAddress(array)) yield break;

        var pointers = memory.ReadBytes(array, count * 8);
        if (pointers == null) yield break;

        for (var i = 0; i < count; i++)
        {
            var node = BitConverter.ToInt64(pointers, i * 8);
            if (GameMemory.IsPlausibleAddress(node))
                yield return node;
        }
    }

    private string ReadName(long addon)
    {
        var bytes = memory.ReadBytes(addon + UnitBaseName, 32);
        if (bytes == null) return string.Empty;
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private string ReadText(long utf8String)
    {
        var pointer = memory.ReadPointer(utf8String);
        var bytes = memory.ReadBytes(pointer, 16);
        if (bytes == null) return string.Empty;
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    // 需要处理的快捷键：不是纯数字。纯数字的单键（1、2、3）国服本来就用 MiedingerMed 显示，不用碰
    private static bool IsHotkeyText(string text) =>
        !string.IsNullOrEmpty(text) && text.Length <= 6 && !text.All(c => c >= '0' && c <= '9');
}
