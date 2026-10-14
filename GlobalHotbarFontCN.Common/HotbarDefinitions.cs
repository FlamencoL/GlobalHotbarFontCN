using System.Linq;

namespace GlobalHotbarFontCN.Common;

/// <summary>
/// 技能栏快捷键的通用定义（国服实测）。
/// 本目录的源文件由各插件项目直接包含编译（不单独编成程序集），只能使用 .NET Framework 4.7.2 也支持的 API。
/// </summary>
internal static class HotbarDefinitions
{
    /// <summary>1~10 号普通技能栏 + 宠物技能栏的界面名称</summary>
    public static readonly string[] HotbarAddonNames =
    {
        "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04",
        "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09",
        "_ActionBarEx",
    };

    /// <summary>一号技能栏的界面名称（调试、诊断时用它做样本）</summary>
    public const string PrimaryHotbarName = "_ActionBar";

    /// <summary>
    /// 技能格子组件里快捷键文字节点的 NodeId。技能消耗、充能层数、冷却秒数是别的节点，靠它排除。
    /// 结构：技能栏 → 每个技能格子组件 → 组件里 NodeId 2 的文字节点，只有一层组件。
    /// </summary>
    public const uint HotkeyNodeId = 2;

    /// <summary>
    /// 文字节点 TextFlags 的这一位：国服对含非数字字符的文字会无视节点设置的字体、改用中文字体显示，
    /// 打开这一位后才按节点设置的字体显示。写入后立即生效。游戏里这一位的正式含义未知（逐位测试得出）。
    /// </summary>
    public const ushort KeepFontFlag = 0x2000;

    /// <summary>快捷键文字节点原本的字体：MiedingerMed（FontType 1）</summary>
    public const byte OriginalFontType = 1;

    /// <summary>快捷键文字最长的字符数（如 cas1、ºº1 这类组合也不会超过），用来排除其他较长的文字</summary>
    public const int MaxHotkeyLength = 6;

    /// <summary>
    /// 需要处理的快捷键文字：不是纯数字。纯数字的单键（1、2、3）国服本来就用 MiedingerMed 正常显示；
    /// 其余的（sR、cQ、小键盘的 º1 等）会被换成中文字体。
    /// </summary>
    public static bool IsHotkeyText(string text) =>
        !string.IsNullOrEmpty(text) && text.Length <= MaxHotkeyLength && !text.All(c => c >= '0' && c <= '9');
}
