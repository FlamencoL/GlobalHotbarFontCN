using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HotbarFont;

/// <summary>
/// 调试：为 ACT 版找"界面管理器"的位置。ACT 插件要从游戏自己的已加载界面列表里找 _ActionBar，
/// 不能直接在内存里搜名字（可能搜到已释放的旧界面，往里写会破坏内存）。
///
/// /hbfont actsig：
/// 1. 在游戏模块里找存放 AtkStage 指针的静态变量（偏移）；
/// 2. 找引用这个静态变量的指令，打印附近字节，用来写特征码；
/// 3. 量出 AtkStage → 界面管理器 → 已加载界面列表 的偏移，并确认列表里能找到 _ActionBar。
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

        // 2. 引用静态变量的 RIP 相对寻址 mov 指令：[REX] 8B ModRM disp32，ModRM 的 mod=00 rm=101
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

        // 3. AtkStage → 界面管理器 → 已加载界面列表
        var manager = stage->RaptureAtkUnitManager;
        Print($"AtkStage.RaptureAtkUnitManager = +0x{(byte*)&stage->RaptureAtkUnitManager - (byte*)stage:X} → 0x{(nint)manager:X}");

        var list = &manager->AtkUnitManager.AllLoadedUnitsList;
        Print($"AllLoadedUnitsList = 界面管理器 +0x{(byte*)list - (byte*)manager:X}，Count = 列表 +0x{(byte*)&list->Count - (byte*)list:X}，Count={list->Count}");

        // 在列表里找 _ActionBar 的指针，确定元素数组的起点
        var addon = (nint)Plugin.GameGui.GetAddonByName("_ActionBar", 1);
        for (var off = 0; off < 0x808; off += 8)
        {
            if (*(nint*)((byte*)list + off) != addon) continue;
            Print($"_ActionBar 在列表 +0x{off:X} 处（元素数组从 +0x8 开始的话是第 {(off - 8) / 8} 个）");
            return;
        }
        Print("列表前 0x808 字节里没找到 _ActionBar 的指针");
    }

    private static void Print(string message) => Plugin.Chat.Print($"[HotbarFont] {message}");
}
