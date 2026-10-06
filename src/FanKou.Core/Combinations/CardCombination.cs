using System.Collections.Immutable;
using FanKou.Core.Cards;

namespace FanKou.Core.Combinations;

/// <summary>
/// 一手已经成型并且在本局中定过性的牌。
/// 构造时牌会按 <see cref="Card.Id"/> 排好序，所以相等性判断是逐张的结构比较。
/// </summary>
public sealed class CardCombination : IEquatable<CardCombination>
{
    private CardCombination(
        ComboType type,
        ImmutableArray<Card> cards,
        Suit group,
        bool isTrump,
        int pairCount,
        int key)
    {
        Type = type;
        Cards = cards;
        Group = group;
        IsTrump = isTrump;
        PairCount = pairCount;
        Key = key;
    }

    public ComboType Type { get; }

    /// <summary>按 Id 排好序的牌。领出顺序在出牌记录里另行保存，不靠这里。</summary>
    public ImmutableArray<Card> Cards { get; }

    /// <summary>跟牌花色义务用的组。等于牌的花色，王是 Joker 组。</summary>
    public Suit Group { get; }

    /// <summary>整手牌是不是全是主牌。杀牌判定和主副对比靠它。</summary>
    public bool IsTrump { get; }

    /// <summary>单张 0、对子 1、拖拉机为连对的对数。</summary>
    public int PairCount { get; }

    /// <summary>
    /// 比大小用的键。单张/对子是牌力次序 <c>CardIdentity.Order</c>；
    /// 拖拉机是顶端那对子的 <c>TractorRank</c>（自然序）。
    /// </summary>
    public int Key { get; }

    public int Length => Cards.Length;

    public int Points => Cards.Sum(c => c.Points);

    /// <summary>
    /// 凑不成任何牌型的散牌，比如没牌可跟时随便垫的两张。
    /// 这种牌**永远赢不了**（见 <see cref="Play.TrickEvaluator"/>），但出它是合法的。
    /// </summary>
    public static CardCombination Shapeless(IEnumerable<Card> cards, bool isTrump, Suit group, int key = 0) =>
        Create(ComboType.Invalid, cards, group, isTrump, pairCount: 0, key: key);

    internal static CardCombination Create(
        ComboType type,
        IEnumerable<Card> cards,
        Suit group,
        bool isTrump,
        int pairCount,
        int key) =>
        new(type, [.. cards.OrderBy(c => c.Id)], group, isTrump, pairCount, key);

    public string Describe() => Type switch
    {
        ComboType.Single => $"单张 {CardText.Format(Cards)}",
        ComboType.Pair => $"对子 {CardText.Format(Cards)}",
        ComboType.Tractor => $"{PairCount} 连对 {CardText.Format(Cards)}",
        _ => CardText.Format(Cards),
    };

    public override string ToString() => Describe();

    public bool Equals(CardCombination? other)
    {
        if (other is null || ReferenceEquals(this, other))
        {
            return ReferenceEquals(this, other);
        }

        return Type == other.Type && Cards.AsSpan().SequenceEqual(other.Cards.AsSpan());
    }

    public override bool Equals(object? obj) => Equals(obj as CardCombination);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        foreach (var card in Cards)
        {
            hash.Add(card.Id);
        }

        return hash.ToHashCode();
    }
}
