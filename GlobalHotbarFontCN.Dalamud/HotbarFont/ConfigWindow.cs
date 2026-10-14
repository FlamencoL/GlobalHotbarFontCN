using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace HotbarFont;

/// <summary>设置窗口：插件列表里的"设置"按钮和 /hbfont config 都打开它。</summary>
public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Hotbar Font 设置###HotbarFontConfig", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
    }

    public override void Draw()
    {
        var config = Plugin.Config;
        if (!config.IsFontTypeValid()) return;

        var changed = false;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用", ref enabled))
        {
            config.Enabled = enabled;
            changed = true;
        }

        if (ImGui.BeginCombo("字体", Plugin.FontNames[config.FontType]))
        {
            for (var i = 0; i < Plugin.FontNames.Length; i++)
            {
                if (ImGui.Selectable(Plugin.FontNames[i], config.FontType == i))
                {
                    config.FontType = i;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        var modifierSymbols = config.ModifierSymbols;
        if (ImGui.Checkbox("使用 MiedingerMed 字体时修饰键显示为符号", ref modifierSymbols))
        {
            config.ModifierSymbols = modifierSymbols;
            changed = true;
        }

        var uppercase = config.Uppercase;
        if (ImGui.Checkbox("转为大写（a1 → A1）", ref uppercase))
        {
            config.Uppercase = uppercase;
            changed = true;
        }

        if (ImGui.Button("手动刷新"))
            plugin.ApplyToAllBars();

        if (changed)
        {
            config.Save();
            plugin.ApplyToAllBars();
        }
    }
}
