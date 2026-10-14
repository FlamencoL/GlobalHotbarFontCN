# ACT 版的特征码与偏移维护

ACT 版（本仓库的 `GlobalHotbarFontCN.ACT`）在游戏进程外跨进程读写内存，用不了 ClientStructs，
特征码和结构偏移定义在通用目录的 [GameOffsets.cs](../GlobalHotbarFontCN.Common/GameOffsets.cs)。
游戏更新后如果 ACT 版失效（页签状态显示"特征码没有匹配"或"布局不符"，或快捷键没有变化），用卫月版测试版的调试命令检查。

调试命令只在卫月版的测试版里有（非 v 标签的 CI 构建，或本地 Debug 编译），代码在 `GlobalHotbarFontCN.Dalamud/HotbarFont/DevTools/`。
它们用 ClientStructs 实测每一项，和 `GameOffsets` 对比，逐项标出 `✓ 一致` 或 `✗ 不一致`；把不一致的项改成实测值即可。

## /hbfont actsig：界面管理器

- 每条 `StageSignatures` 是否还能解析到存放 `AtkStage` 指针的静态变量（`✓ 有效` / `✗ 失效`）
- `StageUnitManager`、`UnitManagerAllLoaded`、`UnitListCount`、`UnitListEntries`

特征码失效时，用输出里"静态变量 +0x… 的引用 +0x…："各行的指令字节写新的：前 3 字节 `48 8B 05` 后面 4 字节是相对地址，
换成 `?? ?? ?? ??`，再保留后面 8 字节左右能区分的指令。ACT 版要求所有特征码解析出同一个静态变量，所以挑的几条都必须引用同一个变量。

## /hbfont actinfo：技能栏 → 快捷键文字节点

- `UnitBaseName`、`UnitBaseUldManager`、`UldNodeList`、`UldNodeListCount`
- `NodeId`、`NodeType`、`ComponentNodeComponent`、`ComponentUldManager`
- `TextNodeFlags`、`TextNodeText`
- `Utf8String` 布局（`Str*`）：按定义读出各字段，看是否自洽（`✓ 与布局一致` / `✗ 与布局不符`）
- `路径：`：遍历结构应为 技能栏 → 组件节点 → NodeId 2 的文字节点，只有一层组件

## /hbfont rawflag [off]：验证"只写标志位"

模拟 ACT 版：只往一号技能栏快捷键的 `TextFlags` 写 `KeepFontFlag`，不调用任何游戏函数。测试前先用 `/hbfont` 关闭卫月版的功能。
字体立即变成 MiedingerMed，说明 ACT 版的做法仍然有效。
