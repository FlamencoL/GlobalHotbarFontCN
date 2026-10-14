# GlobalHotbarFontCN

将国服技能栏快捷键字体对齐国际服风格。

国服技能栏上带修饰键、字母或小键盘键的快捷键（如 `sR`、`cQ`、`º1`）会显示为中文黑体。这里的插件让它们改用游戏内置的 MiedingerMed 字体显示，并可将修饰键显示为国际服的符号（Ctrl 为小号 C、Shift 为上箭头等）。

提供两个版本，功能核心相同，选一个使用即可，**不要同时使用**：

| | [ACT 版](GlobalHotbarFontCN.ACT) | [卫月版](GlobalHotbarFontCN.Dalamud) |
|---|---|---|
| 安装方式 | 从 [Releases](https://github.com/FlamencoL/GlobalHotbarFontCN/releases) 下载 DLL，加到 ACT 插件列表 | 订阅插件源后在卫月插件安装器中安装 |
| MiedingerMed 字体 | ✅ | ✅ |
| 修饰键显示为国际服符号 | ✅（默认开启） | ✅（默认开启） |
| 切换其他字体、转为大写 | ❌ | ✅ |
| 生效时机 | 每秒检查一次 | 技能栏刷新时立即处理 |

两个版本共用的定义（技能栏、修饰键符号表、游戏结构偏移）放在 [GlobalHotbarFontCN.Common](GlobalHotbarFontCN.Common)，由两个项目直接包含编译。维护文档在 [docs](docs)：

- [modifier-symbols.md](docs/modifier-symbols.md)：国服 MiedingerMed 字体中的修饰键符号编码
- [act-offsets.md](docs/act-offsets.md)：ACT 版所需特征码与偏移的检查和更新方法

两个版本使用同一个版本号，推送 `v` 开头的四位版本标签（如 `v1.0.1.5`）时一起发布：卫月版发布到插件源，Release 里附上 ACT 版 DLL 和卫月版插件包。版本号取自标签，发布时不需要改代码；[Directory.Build.props](Directory.Build.props) 里的版本号只用于测试版和本地编译。

## 许可证

本仓库代码以 [AGPL-3.0](LICENSE) 许可证发布。

`GlobalHotbarFontCN.ACT/Lib/Advanced Combat Tracker.dll` 是 ACT 本体，仅用于编译时引用，不适用本许可证，版权归其作者所有。

## 免责声明

本插件为第三方工具，与 Square Enix 及游戏运营方无关，使用风险自负。
