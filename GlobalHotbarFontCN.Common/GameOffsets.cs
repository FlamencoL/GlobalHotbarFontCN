namespace GlobalHotbarFontCN.Common;

/// <summary>
/// 游戏界面结构的特征码与偏移（国服实测），供在游戏进程外按地址读写内存时使用。
/// 游戏大版本更新后可能变化，需要重新测量。
/// </summary>
internal static class GameOffsets
{
    // ---- 定位 AtkStage：mov rax, [rip+disp32]，disp32 指向存放 AtkStage 指针的静态变量 ----

    /// <summary>引用 AtkStage 静态变量的指令特征码。所有特征码必须解析出同一个静态变量</summary>
    public static readonly string[] StageSignatures =
    {
        "48 8B 05 ?? ?? ?? ?? 4C 8B 40 18 45 8B 40 18",
        "48 8B 05 ?? ?? ?? ?? 48 8B D9 8B FA 48 8B 48 18",
    };

    /// <summary>特征码里 disp32 的位置和指令长度：静态变量地址 = 匹配处 + 7 + disp32</summary>
    public const int SignatureDisplacement = 3;
    public const int SignatureInstructionLength = 7;

    // ---- AtkStage → 已加载界面列表 ----
    public const int StageUnitManager = 0x20;       // AtkStage.RaptureAtkUnitManager
    public const int UnitManagerAllLoaded = 0x6900; // AtkUnitManager.AllLoadedUnitsList
    public const int UnitListEntries = 0x8;         // AtkUnitList 元素数组（AtkUnitBase* × UnitListCapacity）
    public const int UnitListCapacity = 256;        // 元素数组的容量
    public const int UnitListCount = 0x808;         // AtkUnitList.Count（u16）

    // ---- 界面 → 节点 ----
    public const int UnitBaseName = 0x8;            // AtkUnitBase.Name（char[UnitBaseNameLength]）
    public const int UnitBaseNameLength = 32;
    public const int UnitBaseUldManager = 0x28;     // AtkUnitBase.UldManager
    public const int UldNodeList = 0x50;            // AtkUldManager.NodeList（AtkResNode**）
    public const int UldNodeListCount = 0x42;       // AtkUldManager.NodeListCount（u16）
    public const int NodeId = 0x8;                  // AtkResNode.NodeId（u32）
    public const int NodeType = 0x40;               // AtkResNode.Type（u16）；文字节点为 TextNodeType，组件节点 ≥ ComponentNodeTypeMin
    public const int ComponentNodeTypeMin = 1000;
    public const int ComponentNodeComponent = 0xC0; // AtkComponentNode.Component
    public const int ComponentUldManager = 0x8;     // AtkComponentBase.UldManager

    // ---- 文字节点 ----
    public const int TextNodeType = 3;
    public const int TextNodeText = 0xD0;           // AtkTextNode.NodeText（Utf8String）
    public const int TextNodeFlags = 0x170;         // AtkTextNode.TextFlags（u16）

    // ---- Utf8String（快捷键文字实测）----
    public const int StrPointer = 0x00;             // 文字指针
    public const int StrBufSize = 0x08;             // 缓冲区容量（64）
    public const int StrBufUsed = 0x10;             // 已用字节（含结尾 0）
    public const int StrLength = 0x18;              // 文字长度（快捷键里为 0，游戏没维护）
    public const int StrIsEmpty = 0x20;
    public const int StrIsUsingInlineBuffer = 0x21;
    public const int StrInlineBuffer = 0x22;        // 内联缓冲区
    public const int StrInlineBufferSize = 0x40;
}
