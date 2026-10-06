namespace FanKou.Core.Trump;

/// <summary>一张牌在**本局**里扮演的角色。同一张 ♣5，主花色不同时角色就不同。</summary>
public enum CardRole : byte
{
    /// <summary>副牌（既不是主花色，也不是级牌，也不是王）。</summary>
    OffSuit = 0,

    /// <summary>主花色里的普通牌。</summary>
    TrumpSuit = 1,

    /// <summary>副级牌，例如主红桃 5 时的黑桃 5 / 梅花 5 / 方块 5。文档第 62 行称"副 5"。</summary>
    SubLevel = 2,

    /// <summary>主级牌，例如主红桃 5 时的红桃 5。文档第 62 行称"主 5"。</summary>
    MainLevel = 3,

    /// <summary>小无主专用：所有花色的级牌同为一级，不分主副（文档第 64 行）。</summary>
    LevelCard = 4,

    SmallJoker = 5,
    BigJoker = 6,
}

/// <summary>
/// 牌力的次序值。这张表逐条对应规则文档第 62 行的原文：
/// "就是大王，小王，主 5，副 5，然后就是主花色的 AKQJ～432，然后是其它花色的 AKQ～432"。
///
/// 注意一个反直觉但文档写明了的点：**副 5 大于主花色的 A**。
/// 副 5 本身是主牌（第 63 行"所有其它花色 5 都是副主"），所以这与第 30 行
/// "主对肯定比副对大"不冲突。
/// </summary>
public static class TrumpOrder
{
    public const int BigJoker = 10_000;
    public const int SmallJoker = 9_000;

    /// <summary>主花色级牌，文档称"主 5"。</summary>
    public const int MainLevel = 8_000;

    /// <summary>其它花色级牌，文档称"副 5"。小无主时与主级牌同级，也用这个值。</summary>
    public const int SubLevel = 7_000;

    /// <summary>主花色普通牌的基准，实际值 = 基准 + 自然序（2 是最小、A 是最大）。</summary>
    public const int TrumpSuitBase = 5_000;

    /// <summary>副牌基准。其它花色之间同级（文档第 62 行"其它花色同级"）。</summary>
    public const int OffSuitBase = 1_000;
}
