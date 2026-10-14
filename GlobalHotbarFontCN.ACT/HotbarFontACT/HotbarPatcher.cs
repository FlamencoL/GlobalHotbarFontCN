using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GlobalHotbarFontCN.Common;
using static GlobalHotbarFontCN.Common.GameOffsets;

namespace GlobalHotbarFontCN.ACT;

/// <summary>
/// 给技能栏快捷键节点打开 TextFlags 的 KeepFontFlag，让国服按节点设置的 MiedingerMed 字体显示，而不是换成中文黑体。
///
/// 不调用游戏函数（跨进程做不到），只直接改内存：标志位实测写入后字体立即生效。
/// "修饰键符号"功能直接改写快捷键文字的 Utf8String（如 cs1 → ½1），写入前核对布局和缓冲区容量。
/// 特征码和偏移见 GlobalHotbarFontCN.Common/GameOffsets.cs。
/// </summary>
internal sealed class HotbarPatcher
{
    // 只写 TextFlags 的高字节，减少和游戏同时写同一处的冲突
    private const int TextNodeFlagsHighByte = TextNodeFlags + 1;
    private const byte KeepFontBit = HotbarDefinitions.KeepFontFlag >> 8;

    // 节点列表长度的合理上限，超出说明读到的不是有效数据
    private const int MaxNodeListCount = 1000;

    // 64 位指针的字节数（界面列表、节点列表都是指针数组）
    private const int PointerSize = 8;

    // 读节点头部时读到 Type（u16）为止，这样一次读取就能拿到 NodeId 和 Type
    private const int NodeHeaderLength = NodeType + sizeof(ushort);

    private static readonly HashSet<string> HotbarNames = new(HotbarDefinitions.HotbarAddonNames);

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
                var disp = BitConverter.ToInt32(memory.ReadBytes(hit + SignatureDisplacement, 4) ?? new byte[4], 0);
                slots.Add(hit + SignatureInstructionLength + disp);
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
        if (count <= 0 || count > UnitListCapacity) return -1;

        var entries = memory.ReadBytes(list + UnitListEntries, count * PointerSize);
        if (entries == null) return -1;

        var changed = 0;
        for (var i = 0; i < count; i++)
        {
            var addon = BitConverter.ToInt64(entries, i * PointerSize);
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
            var header = memory.ReadBytes(node, NodeHeaderLength);
            if (header == null || BitConverter.ToUInt16(header, NodeType) < ComponentNodeTypeMin) continue; // 只看组件节点

            var component = memory.ReadPointer(node + ComponentNodeComponent);
            if (!GameMemory.IsPlausibleAddress(component)) continue;

            foreach (var child in ReadNodeList(component + ComponentUldManager))
            {
                var childHeader = memory.ReadBytes(child, NodeHeaderLength);
                if (childHeader == null
                    || BitConverter.ToUInt32(childHeader, NodeId) != HotbarDefinitions.HotkeyNodeId
                    || BitConverter.ToUInt16(childHeader, NodeType) != TextNodeType)
                    continue;

                var text = ReadText(child + TextNodeText);
                if (!HotbarDefinitions.IsHotkeyText(text)) continue;

                var flags = memory.ReadByte(child + TextNodeFlagsHighByte);
                if (flags < 0) continue;

                var wanted = font ? flags | KeepFontBit : flags & ~KeepFontBit;
                if (wanted != flags && memory.WriteByte(child + TextNodeFlagsHighByte, (byte)wanted))
                    changed++;

                // 修饰键符号：开启时 cs1 → ½1；关闭时换回字母。游戏刷新技能栏会写回原文，下一次检查再换
                var wantedText = symbols ? ModifierSymbols.ToSymbols(text) : ModifierSymbols.FromSymbols(text);
                if (wantedText == text) continue;
                if (WriteText(child + TextNodeText, text, wantedText))
                    changed++;
                else
                    skipped++;
            }
        }
        return changed;
    }

    // ---- 直接改写 Utf8String（不调用游戏函数），布局见 GameOffsets 的 Str* ----

    /// <summary>最近一次布局不符时的实际字段值（诊断用）</summary>
    public string LastMismatch { get; private set; }

    /// <summary>
    /// 先核对布局（已用字节、长度必须和读到的原文一致），再确认缓冲区放得下，才写入；任何一项不符就不写，返回 false。
    /// </summary>
    private bool WriteText(long utf8String, string oldText, string newText)
    {
        var header = memory.ReadBytes(utf8String, StrInlineBuffer);
        if (header == null) return false;

        var pointer = BitConverter.ToInt64(header, StrPointer);
        var bufSize = BitConverter.ToInt64(header, StrBufSize);
        var bufUsed = BitConverter.ToInt64(header, StrBufUsed);
        var length = BitConverter.ToInt64(header, StrLength);

        var oldBytes = Encoding.UTF8.GetBytes(oldText);
        var newBytes = Encoding.UTF8.GetBytes(newText);
        // 技能栏快捷键的文字长度字段实测为 0（游戏没维护），所以长度为 0 也算布局正确
        if (bufUsed != oldBytes.Length + 1 || (length != oldBytes.Length && length != 0))
        {
            // 诊断：记下实际的字段值，显示在插件页签里，用来修正布局
            LastMismatch = $"\"{oldText}\"（{oldBytes.Length} 字节）指针 {(pointer == utf8String + StrInlineBuffer ? "=内联缓冲区" : $"0x{pointer:X}")} " +
                           $"容量 {bufSize} 已用 {bufUsed} 长度 {length} +0x20~0x21 {header[StrIsEmpty]:X2} {header[StrIsUsingInlineBuffer]:X2}";
            return false;
        }

        var capacity = pointer == utf8String + StrInlineBuffer ? Math.Min(bufSize, StrInlineBufferSize) : bufSize;
        if (newBytes.Length + 1 > capacity) return false;

        // 先写内容（带结尾 0），再写已用字节；长度字段原本是 0 就保持 0，不写入游戏本来不会出现的值
        var content = new byte[newBytes.Length + 1];
        Array.Copy(newBytes, content, newBytes.Length);
        return memory.WriteBytes(pointer, content)
            && memory.WriteBytes(utf8String + StrBufUsed, BitConverter.GetBytes((long)content.Length))
            && (length == 0 || memory.WriteBytes(utf8String + StrLength, BitConverter.GetBytes((long)newBytes.Length)));
    }

    private IEnumerable<long> ReadNodeList(long uldManager)
    {
        var count = memory.ReadUInt16(uldManager + UldNodeListCount);
        var array = memory.ReadPointer(uldManager + UldNodeList);
        if (count <= 0 || count > MaxNodeListCount || !GameMemory.IsPlausibleAddress(array)) yield break;

        var pointers = memory.ReadBytes(array, count * PointerSize);
        if (pointers == null) yield break;

        for (var i = 0; i < count; i++)
        {
            var node = BitConverter.ToInt64(pointers, i * PointerSize);
            if (GameMemory.IsPlausibleAddress(node))
                yield return node;
        }
    }

    private string ReadName(long addon)
    {
        var bytes = memory.ReadBytes(addon + UnitBaseName, UnitBaseNameLength);
        if (bytes == null) return string.Empty;
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private string ReadText(long utf8String)
    {
        var pointer = memory.ReadPointer(utf8String);
        // 读满内联缓冲区：快捷键最多 MaxHotkeyLength 个字符，UTF-8 下可能超过 16 字节
        var bytes = memory.ReadBytes(pointer, StrInlineBufferSize);
        if (bytes == null) return string.Empty;
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
