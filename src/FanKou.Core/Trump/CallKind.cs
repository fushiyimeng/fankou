namespace FanKou.Core.Trump;

/// <summary>
/// 叫主的规格。数值就是反主时比大小的优先级：叫主 &lt; 定主 &lt; 小无主 &lt; 大无主。
/// 前两种在本局产生的<see cref="TrumpContext"/>完全相同（都是"某花色为主、当前级牌为主 5"），
/// 区别只在于叫牌时要摆出几张明牌、以及反主时的规格高低。
/// </summary>
public enum CallKind : byte
{
    /// <summary>叫主：1 张王 + 1 张对应色系的级牌（文档第 12 行）。</summary>
    SuitedSuit = 1,

    /// <summary>定主：1 张王 + 2 张同花色的级牌（文档第 12 行）。</summary>
    SuitedBoth = 2,

    /// <summary>小无主：两张小王。第一版不做。</summary>
    SmallNoTrump = 3,

    /// <summary>大无主：两张大王。第一版不做。</summary>
    BigNoTrump = 4,
}
