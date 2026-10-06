namespace FanKou.Core.Cards;

/// <summary>
/// 牌面点数。枚举值刻意等于自然序位次（2=2 … A=14，小王=15，大王=16），
/// 这样 <see cref="CardExtensions.NaturalIndex"/> 只是减 2，不会出现映射表写错的可能。
/// 注意：级牌（5/10/K）在牌桌上的大小会随本局主牌上下文变化，
/// 那种"当前身份的次序"由 <c>CardIdentity.Order</c> 表达，与此处的自然序无关。
/// </summary>
public enum Rank : byte
{
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14,
    SmallJoker = 15,
    BigJoker = 16,
}

public static class Ranks
{
    /// <summary>2 ~ A，不含大小王。</summary>
    public static readonly Rank[] Standard =
    [
        Rank.Two, Rank.Three, Rank.Four, Rank.Five, Rank.Six, Rank.Seven, Rank.Eight,
        Rank.Nine, Rank.Ten, Rank.Jack, Rank.Queen, Rank.King, Rank.Ace,
    ];

    /// <summary>拖拉机靠自然序判断相邻，所以王也在这条序列的末尾。</summary>
    public static readonly Rank[] WithJokers = [.. Standard, Rank.SmallJoker, Rank.BigJoker];

    public const int NaturalCount = 15; // 2..A 共 13 张 + 小王 + 大王
}
