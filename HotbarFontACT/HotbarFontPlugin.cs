using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace GlobalHotbarFontCN_ACT;

/// <summary>
/// ACT 插件入口：把国服技能栏上带字母、修饰键或小键盘键的快捷键改用 MiedingerMed 字体显示（与国际服一致）。
///
/// 每秒检查一次：找游戏进程 → 用特征码定位界面管理器（每个进程只做一次）→ 给所有技能栏的快捷键节点打开标志位。
/// 技能栏在登录、换角色时会重建，所以要持续检查，新建的节点在一秒内就会被处理。
/// </summary>
public sealed class HotbarFontPlugin : UserControl, IActPluginV1
{
    private const int IntervalMs = 1000;

    private readonly CheckBox enabledBox = new() { Text = "启用（技能栏快捷键改用 MiedingerMed 字体）", AutoSize = true, Location = new Point(12, 12) };
    private readonly CheckBox symbolsBox = new() { Text = "修饰键显示为国际服符号（实验）", AutoSize = true, Location = new Point(12, 40) };
    private readonly Label statusLabel = new() { AutoSize = true, Location = new Point(12, 72) };
    private readonly Label noteLabel = new()
    {
        AutoSize = true,
        Location = new Point(12, 104),
        ForeColor = SystemColors.GrayText,
        Text = "修饰键符号：Ctrl 显示为小号 C、Shift 显示为上箭头等；游戏刷新技能栏时会短暂显示原文。\n" +
               "游戏大版本更新后如果失效，请等待插件更新。",
    };

    private System.Threading.Timer timer;
    private int ticking;

    private GameMemory memory;
    private HotbarPatcher patcher;
    private bool applied;
    private volatile bool enabled = true;
    private volatile bool symbols;
    private string lastStatus;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Advanced Combat Tracker", "Config", "GlobalHotbarFontCN_ACT.txt");

    // 插件列表里显示的固定描述；运行状态只显示在插件页签里
    private const string Description = "国服技能栏快捷键改用 MiedingerMed 字体（与国际服一致）";

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        pluginStatusText.Text = Description;

        LoadConfig();
        enabledBox.Checked = enabled;
        symbolsBox.Checked = symbols;
        enabledBox.CheckedChanged += (_, _) =>
        {
            enabled = enabledBox.Checked;
            SaveConfig();
        };
        symbolsBox.CheckedChanged += (_, _) =>
        {
            symbols = symbolsBox.Checked;
            SaveConfig();
        };

        Controls.Add(enabledBox);
        Controls.Add(symbolsBox);
        Controls.Add(statusLabel);
        Controls.Add(noteLabel);
        // 页签第一次显示时才创建窗口句柄，在那之前更新的状态到这时补上
        HandleCreated += (_, _) => statusLabel.Text = $"状态：{lastStatus}";
        Dock = DockStyle.Fill;
        pluginScreenSpace.Text = "Hotbar Font";
        pluginScreenSpace.Controls.Add(this);

        SetStatus("正在等待游戏进程…");
        timer = new System.Threading.Timer(_ => Tick(), null, 0, IntervalMs);
    }

    public void DeInitPlugin()
    {
        // 停止定时器并等正在执行的一次结束，再把改过的标志位恢复
        using (var done = new ManualResetEvent(false))
        {
            timer?.Dispose(done);
            done.WaitOne(3000);
        }

        try
        {
            if (applied && memory != null && !memory.HasExited)
                patcher?.Run(font: false, symbols: false, out _);
        }
        catch
        {
            // 退出时恢复失败不影响 ACT 关闭
        }

        memory?.Dispose();
        memory = null;
    }

    private void Tick()
    {
        // 上一次还没执行完（比如首次扫描特征码较慢）就跳过本次
        if (Interlocked.Exchange(ref ticking, 1) == 1) return;
        try
        {
            if (!EnsureAttached()) return;

            if (!enabled)
            {
                if (applied)
                {
                    patcher.Run(font: false, symbols: false, out _);
                    applied = false;
                }
                SetStatus("已关闭");
                return;
            }

            // 每次都按当前设置执行：关掉"修饰键符号"时会把符号换回字母
            var changed = patcher.Run(font: true, symbols, out var skipped);
            if (changed < 0)
            {
                SetStatus("已连接游戏，等待登录角色…");
                return;
            }

            applied = true;
            SetStatus(skipped > 0 ? $"运行中（{skipped} 个快捷键的文字布局不符，未替换符号）" : "运行中");
        }
        catch (Exception e)
        {
            SetStatus($"出错：{e.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref ticking, 0);
        }
    }

    /// <summary>确保连接到当前游戏进程并定位好界面管理器。游戏重启后会重新连接。</summary>
    private bool EnsureAttached()
    {
        var process = FindGameProcess();
        if (memory != null && (process == null || memory.Process.Id != process.Id || memory.HasExited))
        {
            memory.Dispose();
            memory = null;
            patcher = null;
            applied = false;
        }

        if (process == null)
        {
            SetStatus("正在等待游戏进程…");
            return false;
        }

        if (memory == null)
        {
            memory = GameMemory.Open(process);
            SetStatus("正在定位界面管理器…");
        }

        if (patcher == null)
        {
            patcher = HotbarPatcher.Create(memory, out var message);
            SetStatus(message);
            if (patcher == null)
            {
                // 特征码失效：断开，避免每秒重复扫描整个模块；游戏重启后会再试
                memory.Dispose();
                memory = null;
                Thread.Sleep(30000);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 优先用 FFXIV 解析插件当前解析的进程（多开时与 ACT 一致），通过反射调用，不需要引用解析插件的 DLL；
    /// 找不到解析插件时按进程名找。
    /// </summary>
    private static Process FindGameProcess()
    {
        try
        {
            foreach (var plugin in ActGlobals.oFormActMain.ActPlugins)
            {
                var obj = plugin.pluginObj;
                if (obj == null || obj.GetType().Name != "FFXIV_ACT_Plugin") continue;

                var repository = obj.GetType().GetProperty("DataRepository")?.GetValue(obj);
                var process = repository?.GetType().GetMethod("GetCurrentFFXIVProcess")?.Invoke(repository, null) as Process;
                if (process != null) return process;
            }
        }
        catch
        {
            // 解析插件版本不同、接口变了时，退回按进程名查找
        }

        return Process.GetProcessesByName("ffxiv_dx11").FirstOrDefault();
    }

    // 定时器在后台线程上调用，控件只能在界面线程上改，所以要转到界面线程。
    // 页签还没显示过（没有窗口句柄）时先只记下，等 HandleCreated 时补上。
    private void SetStatus(string status)
    {
        if (status == lastStatus) return;
        lastStatus = status;

        if (!IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() => statusLabel.Text = $"状态：{status}"));
        }
        catch (InvalidOperationException)
        {
            // 句柄恰好在销毁（ACT 关闭中），忽略
        }
    }

    // 配置文件每行一项：enabled=true / symbols=false。读不到时用默认值（启用、不替换符号）
    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                // 旧版配置只有一行 true / false，表示是否启用
                if (line.Trim() == "false") enabled = false;

                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                var value = parts[1].Trim() == "true";
                switch (parts[0].Trim())
                {
                    case "enabled": enabled = value; break;
                    case "symbols": symbols = value; break;
                }
            }
        }
        catch
        {
            // 读取失败用默认值
        }
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllLines(ConfigPath, new[]
            {
                $"enabled={(enabled ? "true" : "false")}",
                $"symbols={(symbols ? "true" : "false")}",
            });
        }
        catch
        {
            // 保存失败只影响下次启动的默认值
        }
    }
}
