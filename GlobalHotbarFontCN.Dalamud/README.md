# GlobalHotbarFontCN.Dalamud

将国服技能栏快捷键字体对齐国际服风格的卫月（Dalamud）插件 Hotbar Font。

国服技能栏上带修饰键、字母或小键盘键的快捷键会显示为中文黑体。本插件把它们改回游戏内置的其他字体（默认 MiedingerMed，与国际服一致），并可将修饰键显示为国际服的符号、转为大写。

## 安装

通过插件源订阅：游戏内 `/xlsettings` →「测试版」→「自定义插件仓库」中添加

```
https://raw.githubusercontent.com/FlamencoL/FFXIVDalamudPlugins/main/pluginmaster.json
```

保存后在 `/xlplugins` 中搜索 Hotbar Font 安装。

## 使用

- 插件列表中点"设置"，或输入 `/hbfont config` 打开设置窗口，可切换字体、开关修饰键符号和大写转换
- `/hbfont`：开 / 关（关闭后恢复原样）
- `/hbfont fix`：手动重新应用
- `/hbfont debug`：打印一号技能栏快捷键节点的状态，排查问题用

## 文档

- [docs/modifier-symbols.md](../docs/modifier-symbols.md)：国服 MiedingerMed 字体中的修饰键符号编码
- [docs/act-offsets.md](../docs/act-offsets.md)：用本插件测试版的调试命令测量 ACT 版所需的特征码与偏移

## 编译

推送到 GitHub 后由 Actions 自动编译，产物在 Actions 页面下载；推送 `v` 开头的四位版本标签（如 `v1.0.1.5`）会发布到插件源。

- 非 `v` 标签的构建是测试版，带调试命令（`HotbarFont/DevTools/`）；正式版不带
- `GlobalHotbarFontCN.Dalamud/dalamud/Dalamud.dll` 存在时用它编译，否则下载国际服最新版卫月库
- 本地编译需要 .NET 10 SDK，并在 `HotbarFont/HotbarFont.csproj` 中把 `DalamudLibPath` 改成本机卫月库路径

## 许可证

本插件以 [AGPL-3.0](../LICENSE) 许可证发布。
