using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Config;
using FanKou.Core.Trump;

namespace FanKou.Core.Bidding;

/// <summary>
/// 从一手牌里找出所有合法的叫法。规则见文档第 12、54 行：
/// <list type="bullet">
/// <item>叫主 = 1 张王 + 1 张对应色系的级牌</item>
/// <item>定主 = 1 张王 + 2 张同花色的级牌</item>
/// <item>小王配黑桃/梅花（黑王配黑花色），大王配红桃/方块（红王配红花色）</item>
/// </list>
/// 小无主（两张小王）和大无主（两张大王）第一版不开放。
/// </summary>
public static class CallFinder
{
    public static IReadOnlyList<CallOption> FindAll(
        IReadOnlyList<Card> hand,
        ImmutableSortedSet<Rank> levelRanks,
        RuleOptions options)
    {
        var results = new List<CallOption>();
        var seen = new HashSet<string>();

        // 第一版只有一个级牌；抢主局（第二轮）会是两个，这里已经按集合处理了。
        var levelRank = levelRanks.Min;

        foreach (var joker in hand.Where(c => c.IsSmallJoker))
        {
            AddSuited(results, seen, joker, Suits.Black, hand, levelRank);
        }

        foreach (var joker in hand.Where(c => c.IsBigJoker))
        {
            AddSuited(results, seen, joker, Suits.Red, hand, levelRank);
        }

        if (options.AllowNoTrumpCalls)
        {
            var smallJokers = hand.Where(c => c.IsSmallJoker).Take(2).ToArray();
            if (smallJokers.Length == 2)
            {
                Add(results, seen, new CallOption(
                    CallKind.SmallNoTrump, null, [.. smallJokers]));
            }

            var bigJokers = hand.Where(c => c.IsBigJoker).Take(2).ToArray();
            if (bigJokers.Length == 2)
            {
                Add(results, seen, new CallOption(
                    CallKind.BigNoTrump, null, [.. bigJokers]));
            }
        }

        return results;
    }

    /// <summary>这个玩家叫得出牌吗。文档第 51 行："有牌必须叫"。</summary>
    public static bool CanCall(
        IReadOnlyList<Card> hand,
        ImmutableSortedSet<Rank> levelRanks,
        RuleOptions options) =>
        FindAll(hand, levelRanks, options).Count > 0;

    private static void AddSuited(
        List<CallOption> results,
        HashSet<string> seen,
        Card joker,
        Suit[] allowedSuits,
        IReadOnlyList<Card> hand,
        Rank levelRank)
    {
        foreach (var suit in allowedSuits)
        {
            var levelCards = hand.Where(c => c.Suit == suit && c.Rank == levelRank).ToArray();
            if (levelCards.Length == 0)
            {
                continue;
            }

            Add(results, seen, new CallOption(
                CallKind.SuitedSuit, suit, [joker, levelCards[0]]));

            if (levelCards.Length >= 2)
            {
                Add(results, seen, new CallOption(
                    CallKind.SuitedBoth, suit, [joker, levelCards[0], levelCards[1]]));
            }
        }
    }

    private static void Add(List<CallOption> results, HashSet<string> seen, CallOption option)
    {
        var key = $"{option.Kind}|{option.TrumpSuit}|{CardSet.ShapeKey(option.ShowCards)}";
        if (seen.Add(key))
        {
            results.Add(option);
        }
    }
}
