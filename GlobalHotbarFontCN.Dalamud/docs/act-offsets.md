# ACT 版的特征码与偏移维护

ACT 版（本仓库的 `GlobalHotbarFontCN.ACT`）在游戏进程外跨进程读写内存，用不了 ClientStructs，
特征码和结构偏移都写死在 [HotbarPatcher.cs](../../GlobalHotbarFontCN.ACT/HotbarFontACT/HotbarPatcher.cs) 开头。游戏更新后如果 ACT 版失效
（页签状态显示"特征码没有匹配"或"布局不符"，或快捷键没有变化），用本插件测试版的调试命令重新测量。

调试命令只在测试版里有（非 v 标签的 CI 构建，或本地 Debug 编译），代码在 `HotbarFont/DevTools/`。

## /hbfont actsig：界面管理器

| 输出 | ACT 版常量 | 当前值 |
|---|---|---|
| `静态变量 +0x… 的引用 +0x…：48 8B 05 …` 各行的字节 | `StageSignatures` | `48 8B 05 ?? ?? ?? ?? 4C 8B 40 18 45 8B 40 18`<br>`48 8B 05 ?? ?? ?? ?? 48 8B D9 8B FA 48 8B 48 18` |
| `AtkStage.RaptureAtkUnitManager = +0x…` | `StageUnitManager` | `0x20` |
| `AllLoadedUnitsList = 界面管理器 +0x…` | `UnitManagerAllLoaded` | `0x6900` |
| `Count = 列表 +0x…` | `UnitListCount` | `0x808` |
| `_ActionBar 在列表 +0x… 处`（元素数组起点） | `UnitListEntries` | `0x8` |

特征码：从输出的指令字节里挑两条，前 3 字节 `48 8B 05` 后面 4 字节是相对地址，换成 `?? ?? ?? ??`，
再保留后面 8 字节左右能区分的指令。ACT 版要求所有特征码解析出同一个静态变量，所以挑的几条都必须引用同一个变量。

## /hbfont actinfo：技能栏 → 快捷键文字节点

| 输出 | ACT 版常量 | 当前值 |
|---|---|---|
| `AtkUnitBase +0x8 处字符串` | `UnitBaseName` | `0x8` |
| `AtkUnitBase.UldManager` | `UnitBaseUldManager` | `0x28` |
| `AtkUldManager.NodeList` | `UldNodeList` | `0x50` |
| `AtkUldManager.NodeListCount` | `UldNodeListCount` | `0x42` |
| `AtkResNode.NodeId` | `NodeId` | `0x8` |
| `AtkResNode.Type` | `NodeType` | `0x40` |
| `AtkComponentNode.Component` | `ComponentNodeComponent` | `0xC0` |
| `AtkComponentBase.UldManager` | `ComponentUldManager` | `0x8` |
| `AtkTextNode.NodeText` | `TextNodeText` | `0xD0` |
| `AtkTextNode.TextFlags`（取 +1 为高字节） | `TextNodeFlagsHighByte` | `0x171`（`TextFlags` 在 `0x170`） |
| `路径：` | 遍历结构（`PatchAddon`） | 技能栏 → 组件节点 → NodeId 2 的文字节点，一层组件 |

`Utf8String` 的布局（ACT 版改写快捷键文字用）没有对应的调试命令，是在 ACT 版里用诊断信息实测的：
`+0x00` 文字指针、`+0x08` 容量（64）、`+0x10` 已用字节（含结尾 0）、`+0x18` 长度（快捷键里为 0）、`+0x22` 内联缓冲区。
ACT 版遇到布局不符时会在页签显示实际字段值。

## /hbfont rawflag [off]：验证"只写标志位"

模拟 ACT 版：只往一号技能栏快捷键的 `TextFlags` 写 `0x2000`，不调用任何游戏函数。测试前先用 `/hbfont` 关闭本插件功能。
字体立即变成 MiedingerMed，说明 ACT 版的做法仍然有效。
