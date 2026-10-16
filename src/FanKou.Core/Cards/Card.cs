namespace FanKou.Core.Cards;

/// <summary>
/// 一张具体的牌。两副牌里同一个 (Suit, Rank) 会出现两次，用 <paramref name="Copy"/>（0/1）区分。
/// 值类型、可哈希，<see cref="Id"/> 在整副 108 张里唯一，可以直接当字典键。
/// </summary>
public readonly record struct Card(Suit Suit, Rank Rank, byte Copy)
{
    /// <summary>0..107，全局唯一。</summary>
    public int Id => (int)Suit * 32 + (int)Rank * 2 + Copy;

    public bool IsJoker => Rank >= Rank.SmallJoker;

    public bool IsBigJoker => Rank == Rank.BigJoker;

    public bool IsSmallJoker => Rank == Rank.SmallJoker;

    /// <summary>分值：5 = 5 分，10 / K = 10 分，其余 0 分。副牌与主牌同价。</summary>
    public int Points => Rank.Points();

    /// <summary>
    /// 按 <see cref="Id"/> 还原一张牌。联机时两边只传这个整数，不传整个对象。
    /// 注意 Joker 是第 5 个花色（枚举值 4），大王会越过一个 32 的边界，
    /// 所以不能简单地用 <c>id / 32</c> 反算花色。
    /// </summary>
    public static Card FromId(int id)
    {
        var suit = Math.Min(id / 32, 4);
        var rem = id - (suit * 32);
        return new Card((Suit)suit, (Rank)(rem / 2), (byte)(rem % 2));
    }

    public override string ToString() => CardText.Format(this);
}
