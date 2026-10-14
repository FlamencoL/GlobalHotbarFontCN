using System;
using Dalamud.Configuration;
using GlobalHotbarFontCN.Common;

namespace HotbarFont;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>0=Axis 1=MiedingerMed 2=Miedinger 3=TrumpGothic 4=Jupiter 5=JupiterLarge
    /// 默认用技能栏原本的字体 MiedingerMed</summary>
    public int FontType { get; set; } = HotbarDefinitions.OriginalFontType;

    /// <summary>是否把 a1 / cQ 转成 A1 / CQ（国际服样式）</summary>
    public bool Uppercase { get; set; } = true;

    /// <summary>修饰键（c / a / s）显示为国际服的符号；只在 MiedingerMed 字体下用符号，其他字体用大写字母 C / A / S</summary>
    public bool ModifierSymbols { get; set; } = true;

    /// <summary>FontType 是否在 FontNames 范围内（配置文件可能被手动改坏）</summary>
    public bool IsFontTypeValid() => FontType >= 0 && FontType < Plugin.FontNames.Length;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
