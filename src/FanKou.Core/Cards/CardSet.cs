using System.Collections.Immutable;

namespace FanKou.Core.Cards;

/// <summary>把一副手牌当成"多重集合"来用的工具。牌可能重复（两副牌），所以不能只用 HashSet。</summary>
public static class CardSet
{
    public static bool ContainsAll(IReadOnlyList<Card> hand, IReadOnlyList<Card> selected)
    {
        var counts = Count(hand);
        foreach (var card in selected)
        {
            if (!counts.TryGetValue(card.Id, out var left) || left == 0)
            {
                return false;
            }

            counts[card.Id] = left - 1;
        }

        return true;
    }

    /// <summary>hand 去掉 taken 之后的剩余手牌。</summary>
    public static ImmutableArray<Card> Remove(IReadOnlyList<Card> hand, IReadOnlyList<Card> taken)
    {
        var remaining = new List<Card>(hand);
        foreach (var card in taken)
        {
            var index = remaining.FindIndex(c => c.Id == card.Id);
            if (index >= 0)
            {
                remaining.RemoveAt(index);
            }
        }

        return [.. remaining];
    }

    public static Dictionary<int, int> Count(IReadOnlyList<Card> cards)
    {
        var counts = new Dictionary<int, int>(cards.Count);
        foreach (var card in cards)
        {
            counts[card.Id] = counts.GetValueOrDefault(card.Id) + 1;
        }

        return counts;
    }

    /// <summary>
    /// 只看花色和点数、不看是哪一副的那张牌。用来给候选出牌去重——
    /// 两副牌里的两张 ♠5 对玩法来说是完全一样的。
    /// </summary>
    public static string ShapeKey(IEnumerable<Card> cards) =>
        string.Join(",", cards.Select(c => $"{(int)c.Suit}{(int)c.Rank}").Order(StringComparer.Ordinal));

    /// <summary>按花色点数去重，每种保留第一张。</summary>
    public static IEnumerable<Card> DistinctByShape(IEnumerable<Card> cards) =>
        cards.DistinctBy(c => (c.Suit, c.Rank));
}
