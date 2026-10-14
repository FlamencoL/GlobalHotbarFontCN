using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GlobalHotbarFontCN.Common;
using static HotbarFont.ActProbe;

namespace HotbarFont;

/// <summary>
/// 调试：为 ACT 版找"界面管理器"的位置。ACT 插件要从游戏自己的已加载界面列表里找 _ActionBar，
/// 不能直接在内存里搜名字（可能搜到已释放的旧界面，往里写会破坏内存）。
///
/// /hbfont actsig：
/// 1. 在游戏模块里找存放 AtkStage 指针的静态变量（偏移）；
/// 2. 检查 GameOffsets.StageSignatures 是否都能解析到这个静态变量；
/// 3. 找引用这个静态变量的指令，打印附近字节，特征码失效时用来写新的；
/// 4. 量出 AtkStage → 界面管理器 → 已加载界面列表 的偏移，和 GameOffsets 对比，并确认列表里能找到 _ActionBar。
/// </summary>
internal static unsafe class ActSigProbe
{
    public static void Run()
    {
        var stage = AtkStage.Instance();
        var module = Process.GetCurrentProcess().MainModule!;
        var moduleBase = (byte*)module.BaseAddress;
        var moduleSize = module.ModuleMemorySize;
        Print($"AtkStage 0x{(nint)stage:X}，模块基址 0x{(nint)moduleBase:X}");

        // 1. 模块里值等于 AtkStage 指针的 8 字节位置（静态变量）
        var slots = new List<long>();
        for (long i = 0; i + 8 <= moduleSize; i += 8)
        {
            if (*(nint*)(moduleBase + i) == (nint)stage)
                slots.Add(i);
        }
        Print($"存放 AtkStage 指针的静态变量：{string.Join("、", slots.Select(s => $"+0x{s:X}"))}");

        // 2. 通用定义里的特征码能否解析到这个静态变量
        foreach (var signature in GameOffsets.StageSignatures)
        {
            var resolved = new List<long>();
            foreach (var hit in ScanSignature(moduleBase, moduleSize, signature))
            {
                var target = hit + GameOffsets.SignatureInstructionLength + *(int*)(moduleBase + hit + GameOffsets.SignatureDisplacement);
                if (!resolved.Contains(target))
                    resolved.Add(target);
            }
            var ok = resolved.Count == 1 && slots.Contains(resolved[0]);
            Print($"特征码 {signature}：解析到 {(resolved.Count == 0 ? "无" : string.Join("、", resolved.Select(s => $"+0x{s:X}")))} {(ok ? "✓ 有效" : "✗ 失效")}");
        }

        // 3. 引用静态变量的 RIP 相对寻址 mov 指令：[REX] 8B ModRM disp32，ModRM 的 mod=00 rm=101
        foreach (var slot in slots.Take(3))
        {
            var hits = 0;
            for (long i = 0; i + 7 <= moduleSize && hits < 5; i++)
            {
                var p = moduleBase + i;
                if ((p[0] != 0x48 && p[0] != 0x4C) || p[1] != 0x8B || (p[2] & 0xC7) != 0x05) continue;
                if (i + 7 + *(int*)(p + 3) != slot) continue;

                var bytes = string.Join(" ", Enumerable.Range(0, 24).Select(k => p[k].ToString("X2")));
                Print($"静态变量 +0x{slot:X} 的引用 +0x{i:X}：{bytes}");
                hits++;
            }
            if (hits == 0)
                Print($"静态变量 +0x{slot:X} 没找到 mov 引用");
        }

        // 4. AtkStage → 界面管理器 → 已加载界面列表
        var manager = stage->RaptureAtkUnitManager;
        Check("AtkStage.RaptureAtkUnitManager", (byte*)&stage->RaptureAtkUnitManager - (byte*)stage, GameOffsets.StageUnitManager, nameof(GameOffsets.StageUnitManager));

        var list = &manager->AtkUnitManager.AllLoadedUnitsList;
        Check("AtkUnitManager.AllLoadedUnitsList", (byte*)list - (byte*)manager, GameOffsets.UnitManagerAllLoaded, nameof(GameOffsets.UnitManagerAllLoaded));
        Check("AtkUnitList.Count", (byte*)&list->Count - (byte*)list, GameOffsets.UnitListCount, nameof(GameOffsets.UnitListCount));
        Print($"已加载界面数：{list->Count}");

        // 在列表里找 _ActionBar 的指针：位置应落在元素数组（从 UnitListEntries 开始、每项 8 字节）里
        var addon = (nint)Plugin.GameGui.GetAddonByName(HotbarDefinitions.PrimaryHotbarName, 1);
        for (var off = 0; off < GameOffsets.UnitListCount; off += 8)
        {
            if (*(nint*)((byte*)list + off) != addon) continue;
            var ok = off >= GameOffsets.UnitListEntries && (off - GameOffsets.UnitListEntries) % 8 == 0;
            Print($"_ActionBar 在列表 +0x{off:X} 处，按 GameOffsets.UnitListEntries = 0x{GameOffsets.UnitListEntries:X} 是第 {(off - GameOffsets.UnitListEntries) / 8} 个 {(ok ? "✓ 一致" : "✗ 不一致")}");
            return;
        }
        Print("✗ 列表里没找到 _ActionBar 的指针");
    }

    // 在模块内存里找特征码（"??" 为通配），返回匹配处相对模块基址的偏移
    private static List<long> ScanSignature(byte* moduleBase, long moduleSize, string signature)
    {
        var pattern = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b == "??" ? -1 : Convert.ToInt32(b, 16))
            .ToArray();

        var hits = new List<long>();
        for (long i = 0; i + pattern.Length <= moduleSize; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (pattern[j] >= 0 && moduleBase[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                hits.Add(i);
        }
        return hits;
    }
}
