using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GlobalHotbarFontCN.Common;

namespace HotbarFont;

/// <summary>
/// 实验：找国际服修饰键用的特殊字符（Shift 显示为小上箭头、Ctrl 显示为小号 C）。
///
/// /hbfont glyphs：列出国服 MiedingerMed 12 号字体文件里所有非 ASCII 字符的编码和字形尺寸。
/// /hbfont preview 页码：把这些字符按页写到一号技能栏的快捷键上（每格 2 个），直接看字形。
///   预览会覆盖快捷键文字，看完切换一下技能栏页或重新登录即可恢复。
/// </summary>
internal static unsafe class GlyphProbe
{
    private const string FdtPath = "common/font/MiedingerMid_12.fdt";
    private const int PerNode = 2;

    private readonly record struct Glyph(int CodePoint, byte Width, byte Height);

    public static void List()
    {
        var glyphs = ReadGlyphs();
        if (glyphs == null) return;

        var extra = glyphs.Where(g => g.CodePoint > 0x7E).ToList();
        Print($"MiedingerMid_12：共 {glyphs.Count} 个字符，其中非 ASCII {extra.Count} 个（格式 U+编码(宽x高)）");

        // 顺便对比小写 c 和大写 C，看国服字体里小写是不是小号大写
        foreach (var c in "cCsSaA")
        {
            var g = glyphs.FirstOrDefault(x => x.CodePoint == c);
            Print($"  '{c}' {g.Width}x{g.Height}");
        }

        foreach (var chunk in extra.Chunk(8))
            Print(string.Join("  ", chunk.Select(g => $"U+{g.CodePoint:X4}({g.Width}x{g.Height})")));
    }

    public static void Preview(int page)
    {
        var glyphs = ReadGlyphs();
        if (glyphs == null) return;
        var extra = glyphs.Where(g => g.CodePoint > 0x7E).ToList();

        var nodes = HotbarOneNodes();
        if (nodes == null) return;

        var perPage = nodes.Count * PerNode;
        var pages = (extra.Count + perPage - 1) / perPage;
        if (perPage == 0 || page < 1 || page > pages)
        {
            Print($"页码范围 1~{pages}（一号技能栏有 {nodes.Count} 个快捷键，每格 {PerNode} 个字符）");
            return;
        }

        var start = (page - 1) * perPage;
        for (var i = 0; i < nodes.Count; i++)
        {
            var chars = extra.Skip(start + i * PerNode).Take(PerNode).ToList();
            if (chars.Count == 0) break;

            var node = (AtkTextNode*)nodes[i];
            var text = string.Concat(chars.Select(g => char.ConvertFromUtf32(g.CodePoint)));
            Plugin.SetLook(node, Plugin.OriginalFont, true, text);
            Print($"第 {i + 1} 格：{string.Join(" ", chars.Select(g => $"U+{g.CodePoint:X4}"))}");
        }
        Print($"第 {page}/{pages} 页。看完切换技能栏页或重新登录即可恢复快捷键");
    }

    /// <summary>
    /// /hbfont show A2 AA B9 ...：把每个十六进制编码的字符加上它的序号（1、2、3…），依次写到一号技能栏的快捷键上，
    /// 模拟"修饰键符号 + 按键"的样子，和国际服截图对照。序号用来在截图里认出是哪个字符。
    /// </summary>
    public static void Show(string args)
    {
        var codes = new List<int>();
        foreach (var part in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var code) || code is < 0x20 or > 0xFFFF)
            {
                Print("用法：/hbfont show 编码 [编码 ...]（十六进制，如 A2 AA B9）");
                return;
            }
            codes.Add(code);
        }

        var nodes = HotbarOneNodes();
        if (nodes == null) return;

        for (var i = 0; i < codes.Count && i < nodes.Count; i++)
        {
            // 符号后面写序号而不是固定的 1，截图里每格自带编号，不怕格子顺序和遍历顺序对不上
            var text = char.ConvertFromUtf32(codes[i]) + (i + 1);
            Plugin.SetLook((AtkTextNode*)nodes[i], Plugin.OriginalFont, true, text);
            Print($"序号 {i + 1}：U+{codes[i]:X4}");
        }
        Print("看完切换技能栏页或重新登录即可恢复快捷键");
    }

    // 一号技能栏所有快捷键节点（含纯数字），遍历顺序是从最后一格往前，反转成从第一格开始
    private static List<nint>? HotbarOneNodes()
    {
        var addon = (AtkUnitBase*)(nint)Plugin.GameGui.GetAddonByName(HotbarDefinitions.PrimaryHotbarName, 1);
        if (addon == null)
        {
            Print("找不到 _ActionBar");
            return null;
        }

        var nodes = new List<nint>();
        Plugin.ForEachHotkeyNode(addon, t =>
        {
            if (!string.IsNullOrEmpty(t->NodeText.ToString()))
                nodes.Add((nint)t);
        });
        nodes.Reverse();
        return nodes;
    }

    // fdt 结构：文件头 0x08 处是字体表偏移；字体表头 0x04 处是字符数，表头 0x20 字节后是每个 0x10 字节的字符条目。
    // 条目：字符 UTF-8 编码按大端拼成的 u32 | ShiftJis u16 | 贴图编号 u16 | X u16 | Y u16 | 宽 u8 | 高 u8 | 偏移 2 字节
    private static List<Glyph>? ReadGlyphs()
    {
        var data = Plugin.DataManager.GetFile(FdtPath)?.Data;
        if (data == null)
        {
            Print($"读取 {FdtPath} 失败");
            return null;
        }

        var table = BitConverter.ToInt32(data, 0x08);
        var count = BitConverter.ToInt32(data, table + 0x04);
        var glyphs = new List<Glyph>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = table + 0x20 + i * 0x10;
            var utf8 = BitConverter.ToUInt32(data, entry);
            glyphs.Add(new Glyph(DecodeUtf8(utf8), data[entry + 0x0C], data[entry + 0x0D]));
        }
        return glyphs;
    }

    // 把"UTF-8 字节按大端拼成的整数"还原成 Unicode 编码，如 0xC2BA → U+00BA
    private static int DecodeUtf8(uint packed)
    {
        var bytes = BitConverter.GetBytes(packed);
        Array.Reverse(bytes);
        var s = Encoding.UTF8.GetString(bytes.SkipWhile(b => b == 0).ToArray());
        return s.Length == 0 ? 0 : char.ConvertToUtf32(s, 0);
    }

    private static void Print(string message) => Plugin.Chat.Print($"[HotbarFont] {message}");
}
