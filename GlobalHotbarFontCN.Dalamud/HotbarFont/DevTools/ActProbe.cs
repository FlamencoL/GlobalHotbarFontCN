using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GlobalHotbarFontCN.Common;

namespace HotbarFont;

/// <summary>
/// 调试：为 ACT 版收集信息。ACT 插件在游戏进程外，只能跨进程读写内存，拿不到 ClientStructs，
/// 所以在这里用 ClientStructs 量出真实的结构偏移，和通用定义 GameOffsets 逐项对比。
///
/// /hbfont actinfo：打印技能栏 → 快捷键节点各结构的偏移和路径，标出与 GameOffsets 是否一致。
/// /hbfont rawflag [off]：模拟 ACT 版能做的事——只往一号技能栏快捷键节点的 TextFlags 写 KeepFontFlag，
///   不调用 SetFont / SetText，看字体会不会立即生效。测试前先用 /hbfont 关闭插件功能，免得插件自己的处理干扰结果。
/// </summary>
internal static unsafe class ActProbe
{
    private const TextFlags KeepFontFlag = (TextFlags)HotbarDefinitions.KeepFontFlag;

    // 打印一项实测偏移，并和通用定义对比
    internal static void Check(string name, long measured, int defined, string definedName) =>
        Print($"{name} = +0x{measured:X}（GameOffsets.{definedName} = 0x{defined:X} {(measured == defined ? "✓ 一致" : "✗ 不一致")}）");

    public static void Info()
    {
        var addon = (AtkUnitBase*)(nint)Plugin.GameGui.GetAddonByName(HotbarDefinitions.PrimaryHotbarName, 1);
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
        var name = Marshal.PtrToStringAnsi((nint)addon + GameOffsets.UnitBaseName);
        Print($"AtkUnitBase +0x{GameOffsets.UnitBaseName:X}（GameOffsets.UnitBaseName）处字符串：{name} {(name == HotbarDefinitions.PrimaryHotbarName ? "✓ 一致" : "✗ 不一致")}");
        Check("AtkUnitBase.UldManager", Off(addon, &addon->UldManager), GameOffsets.UnitBaseUldManager, nameof(GameOffsets.UnitBaseUldManager));

        var uld = &addon->UldManager;
        Check("AtkUldManager.NodeList", Off(uld, &uld->NodeList), GameOffsets.UldNodeList, nameof(GameOffsets.UldNodeList));
        Check("AtkUldManager.NodeListCount", Off(uld, &uld->NodeListCount), GameOffsets.UldNodeListCount, nameof(GameOffsets.UldNodeListCount));

        // 找快捷键节点，同时记录从技能栏一路下来的路径
        if (!FindHotkey(uld, 0, HotbarDefinitions.PrimaryHotbarName, out var text, out var path, out var component, out var componentNode))
        {
            Print("没有找到带字母的快捷键节点（NodeId 2）");
            return;
        }

        var res = &text->AtkResNode;
        Print($"路径：{path}");
        Check("AtkResNode.NodeId", Off(res, &res->NodeId), GameOffsets.NodeId, nameof(GameOffsets.NodeId));
        Check("AtkResNode.Type", Off(res, &res->Type), GameOffsets.NodeType, nameof(GameOffsets.NodeType));
        Check("AtkComponentNode.Component", Off(componentNode, &componentNode->Component), GameOffsets.ComponentNodeComponent, nameof(GameOffsets.ComponentNodeComponent));
        Check("AtkComponentBase.UldManager", Off(component, &component->UldManager), GameOffsets.ComponentUldManager, nameof(GameOffsets.ComponentUldManager));
        Check("AtkTextNode.TextFlags", Off(text, &text->TextFlags), GameOffsets.TextNodeFlags, nameof(GameOffsets.TextNodeFlags));
        Check("AtkTextNode.NodeText", Off(text, &text->NodeText), GameOffsets.TextNodeText, nameof(GameOffsets.TextNodeText));

        // Utf8String 的字段在 ClientStructs 里有些是属性、取不了地址，按 GameOffsets 的布局直接读，看值是否自洽
        var str = (byte*)&text->NodeText;
        var pointer = *(nint*)(str + GameOffsets.StrPointer);
        var bufSize = *(long*)(str + GameOffsets.StrBufSize);
        var bufUsed = *(long*)(str + GameOffsets.StrBufUsed);
        var length = *(long*)(str + GameOffsets.StrLength);
        var inline = str[GameOffsets.StrIsUsingInlineBuffer];
        var content = Marshal.PtrToStringUTF8(pointer) ?? "";
        var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
        var consistent = bufUsed == bytes + 1 && (length == bytes || length == 0)
                         && (inline == 0 || pointer == (nint)(str + GameOffsets.StrInlineBuffer));
        Print($"Utf8String：指针 → \"{content}\"（{bytes} 字节），容量 {bufSize}，已用 {bufUsed}，长度 {length}，" +
              $"内联 {inline}{(pointer == (nint)(str + GameOffsets.StrInlineBuffer) ? "（指向内联缓冲区）" : "")} {(consistent ? "✓ 与布局一致" : "✗ 与布局不符")}");
        Print($"示例节点 0x{(nint)text:X}：TextFlags=0x{(int)text->TextFlags:X}，FontType={(int)text->FontType}");
    }

    public static void RawFlag(bool on)
    {
        var addon = (AtkUnitBase*)(nint)Plugin.GameGui.GetAddonByName(HotbarDefinitions.PrimaryHotbarName, 1);
        if (addon == null)
        {
            Print("找不到 _ActionBar");
            return;
        }

        var count = 0;
        Plugin.ForEachHotkeyNode(addon, t =>
        {
            if (!HotbarDefinitions.IsHotkeyText(t->NodeText.ToString())) return;

            // 只改字段，不调用任何游戏函数——ACT 版跨进程写内存只能做到这一步
            if (on)
                t->TextFlags |= KeepFontFlag;
            else
                t->TextFlags &= ~KeepFontFlag;
            count++;
        });
        Print($"已直接{(on ? "打开" : "关闭")} {count} 个快捷键的 0x{HotbarDefinitions.KeepFontFlag:X}（未重新排版）。看字体有没有立即变化；" +
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

            if ((ushort)node->Type >= GameOffsets.ComponentNodeTypeMin)
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
            else if (node->Type == NodeType.Text && node->NodeId == HotbarDefinitions.HotkeyNodeId)
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

    internal static void Print(string message) => Plugin.Chat.Print($"[HotbarFont] {message}");
}
