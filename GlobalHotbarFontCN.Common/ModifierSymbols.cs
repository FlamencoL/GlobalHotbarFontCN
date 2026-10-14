using System.Collections.Generic;
using System.Linq;

namespace GlobalHotbarFontCN.Common;

/// <summary>
/// 修饰键符号（国服实测）。
///
/// 国服快捷键开头的修饰键是小写字母，顺序固定为 c(Ctrl) → a(Alt) → s(Shift)，如 cs1、cas1、sR。
/// 国服 MiedingerMed 字体里有国际服用的修饰键符号，借用了 Latin-1 区的字符位；这些符号只在 MiedingerMed 下有意义。
/// </summary>
internal static class ModifierSymbols
{
    /// <summary>修饰键前缀 → 符号</summary>
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["c"] = "¢",   // 小号 C
        ["a"] = "ª",   // 小号 A
        ["s"] = "§",   // 上箭头
        ["ca"] = "¾",  // 小号 C 叠在小号 A 上（尚未核实）
        ["cs"] = "½",  // 小号 C + 上箭头
        ["as"] = "¼",  // 小号 A + 上箭头
        ["cas"] = "¶", // 三条横线
    };

    private static readonly Dictionary<char, string> Reverse = Map.ToDictionary(p => p.Value[0], p => p.Key);

    /// <summary>开头按 c、a、s 顺序出现的小写修饰键有几个；后面必须还剩按键，否则不算修饰键，返回 0</summary>
    public static int PrefixLength(string text)
    {
        var i = 0;
        foreach (var m in "cas")
        {
            if (i < text.Length && text[i] == m)
                i++;
        }
        return i < text.Length ? i : 0;
    }

    /// <summary>修饰键前缀换成符号，如 cs1 → ½1；没有修饰键时原样返回</summary>
    public static string ToSymbols(string text)
    {
        var length = PrefixLength(text);
        return length == 0 ? text : Map[text.Substring(0, length)] + text.Substring(length);
    }

    /// <summary>开头的符号换回修饰键字母，如 ½1 → cs1；开头不是符号时原样返回</summary>
    public static string FromSymbols(string text) =>
        text.Length > 1 && Reverse.TryGetValue(text[0], out var modifiers) ? modifiers + text.Substring(1) : text;
}
