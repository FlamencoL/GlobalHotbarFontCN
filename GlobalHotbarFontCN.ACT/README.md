# GlobalHotbarFontCN.ACT

将国服技能栏快捷键字体对齐国际服风格的 ACT 插件。

国服技能栏上带修饰键、字母或小键盘键的快捷键（如 `sR`、`cQ`、`º1`）会显示为中文黑体。本插件让它们改用游戏内置的 MiedingerMed 字体显示，与国际服一致。

## 安装

1. 从 [Releases](https://github.com/FlamencoL/GlobalHotbarFontCN/releases) 下载 `GlobalHotbarFontCN.ACT.dll`
2. ACT →「插件」→「插件列表」→ 浏览选择该 DLL →「添加/启用插件」
3. 插件页签「Hotbar Font」里可以开关功能，并查看运行状态，设置保存在 DLL 同目录、同名的 `.cfg` 文件（`GlobalHotbarFontCN.ACT.cfg`）

- 需要 64 位 ACT，且 ACT 与游戏以相同权限运行（都用或都不用管理员身份）
- 装有 FFXIV 解析插件时，使用它当前解析的游戏进程；否则按进程名 `ffxiv_dx11` 查找

## 功能

- **MiedingerMed 字体**：MiedingerMed 本身是全大写字形，`sR` 显示出来就是 `SR` 的样子。
- **修饰键显示为国际服符号（默认开启）**：Ctrl 显示为小号 C、Shift 显示为上箭头、Alt 显示为小号 A，组合键有对应的组合符号。游戏刷新技能栏（换页、改键位）时会写回原文，插件在一秒内再换成符号，期间会短暂显示原文。

## 原理

国服对含非数字字符的快捷键文字会无视节点设置的字体，改用中文黑体显示。给快捷键文字节点的 `TextFlags` 打开 `0x2000` 后，游戏就会按节点设置的字体（本来就是 MiedingerMed）显示。

插件在游戏进程外运行，跨进程直接改内存，不修改游戏代码、不调用游戏函数：

1. 用特征码在游戏模块里找到存放 `AtkStage` 指针的静态变量
2. `AtkStage` → 界面管理器 → 已加载界面列表，找出 `_ActionBar`、`_ActionBar01`~`09`、`_ActionBarEx`
3. 遍历每个技能格子组件，找 NodeId 为 2 的文字节点（快捷键），打开 `TextFlags` 的 `0x2000`
4. 开启修饰键符号时，把快捷键文字开头的 `c` / `a` / `s` 改写为字体内置的符号（如 `cs1` → `½1`）；写入前核对文字的存储布局和缓冲区容量，不符就跳过

每秒检查一次，登录、换角色后重建的技能栏会自动处理。关闭功能或卸载插件时会撤销修改。

## 维护

特征码与结构偏移定义在通用目录的 [GameOffsets.cs](../GlobalHotbarFontCN.Common/GameOffsets.cs)，修饰键符号表在 [ModifierSymbols.cs](../GlobalHotbarFontCN.Common/ModifierSymbols.cs)。游戏更新后如果失效，需要重新测量这些值：界面管理器的特征码、`AtkStage` → 已加载界面列表的偏移，以及技能栏 → 快捷键文字节点各结构的偏移。文字布局不符时，插件页签会显示实际字段值。

## 编译

推送到 GitHub 后由 Actions 自动编译；推送 `v` 开头的四位版本标签（如 `v1.0.1.5`）会创建 Release 并附上 DLL。

`Lib/Advanced Combat Tracker.dll` 是编译时引用的 ACT 本体（取自呆萌整合版 `DLibs` 目录），运行时由 ACT 提供，不随插件分发。

## 许可证

本插件以 [AGPL-3.0](../LICENSE) 许可证发布。`Lib/Advanced Combat Tracker.dll` 不适用本许可证，版权归其作者所有。
