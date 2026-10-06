using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Play;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 中等水平的 AI。不做记牌和算命，只会：
/// <list type="bullet">
/// <item>叫主时挑自己主牌最多的那门；牌太烂就让队友叫</item>
/// <item>扣底牌时先丢"没用的小散牌"，主牌、对子、分牌都尽量留着</item>
/// <item>出牌时看得懂队友和对手的区别：队友已经最大就别压，对手最大就想办法抢</item>
/// <item>台面有分才值得动大牌，没分就出小的省着</item>
/// </list>
/// 这个水平足够让对局像个样子，但还远远谈不上"会算牌"。
/// </summary>
public sealed class SimpleBrain(IRandomSource rng) : IPlayerBrain
{
    /// <summary>叫主的牌力低于这个分就让队友来叫（前提是让得出去）。</summary>
    private const double WeakHandThreshold = 12;

    private readonly RandomBrain _fallback = new(rng);

    public CallOption? ChooseCall(CallContext context)
    {
        if (context.Options.Count == 0)
        {
            return null;
        }

        var best = context.Options
            .Select(option => (Option: option, Score: RateCallOption(option, context.Hand, context.LevelRank)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Option.Priority)
            .First();

        if (context.CanPass && best.Score < WeakHandThreshold)
        {
            return null;
        }

        return best.Option;
    }

    public ImmutableArray<Card> ChooseBottomDiscards(BottomContext context)
    {
        var locked = context.Locked.Select(c => c.Id).ToHashSet();

        var discards = context.Hand
            .Where(c => !locked.Contains(c.Id))
            .OrderBy(c => DiscardScore(c, context.Hand, context.Trump))
            .Take(8)
            .ToArray();

        return discards.Length == 8 ? [.. discards] : _fallback.ChooseBottomDiscards(context);
    }

    public CardCombination ChoosePlay(PlayContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return _fallback.ChoosePlay(context);
        }

        return context.IsLeading ? ChooseLead(context) : ChooseFollow(context);
    }

    // ------------------------------------------------------------- 叫主

    private static double RateCallOption(CallOption option, ImmutableArray<Card> hand, Rank levelRank)
    {
        if (option.TrumpSuit is not { } suit)
        {
            // 无主：大无主只有王是主，小无主是王加所有级牌。
            var jokers = hand.Count(c => c.IsJoker);
            var levelCards = hand.Count(c => c.Rank == levelRank);

            return option.Kind == CallKind.BigNoTrump
                ? (jokers * 4.0) + (levelCards * 0.5)
                : ((jokers + levelCards) * 2.0) + (jokers * 1.5);
        }

        var trumps = hand.Where(c => c.IsJoker || c.Suit == suit || c.Rank == levelRank).ToArray();

        var score = trumps.Length * 2.0;
        score += trumps.Count(c => c.IsJoker) * 3.5;
        score += trumps.GroupBy(c => (c.Suit, c.Rank)).Count(g => g.Count() >= 2) * 1.5;
        score += hand.Count(c => c.Suit == suit) * 0.5;

        return score;
    }

    // ------------------------------------------------------------- 扣底牌

    /// <summary>分数越低越该扣掉。</summary>
    private static double DiscardScore(Card card, ImmutableArray<Card> hand, TrumpContext trump)
    {
        var identity = IdentityResolver.Resolve(card, trump);
        var score = (double)card.Rank.NaturalIndex();   // 小牌先走

        if (identity.IsTrump)
        {
            score += 90;                                 // 主牌基本不扣
        }

        if (identity.IsLevelCard)
        {
            score += 15;
        }

        if (hand.Count(c => c.Suit == card.Suit && c.Rank == card.Rank) >= 2)
        {
            score += 35;                                 // 对子留着
        }

        // 分牌尽量别埋：庄家埋了分，最后一手被闲家赢走就是翻倍送给对面。
        score += card.Points * 3;

        // 这门牌本来就少，丢了容易做空门，以后可以用主牌杀。
        if (hand.Count(c => c.Suit == card.Suit) <= 2)
        {
            score -= 14;
        }

        return score;
    }

    // ------------------------------------------------------------- 出牌

    private CardCombination ChooseFollow(PlayContext context)
    {
        var trick = context.Info.CurrentTrick;
        var tablePoints = trick.Sum(play => play.Combo.Points);
        var winner = context.Info.CurrentTrickWinner;
        var partnerIsWinning = winner is { } seat && seat.TeamOf() == context.Seat.TeamOf();

        return PickBest(context, candidate =>
        {
            var wins = WouldWin(candidate, context);
            var value = 0.0;

            if (wins)
            {
                // 队友已经最大了，再压过去纯属浪费大牌。
                value += partnerIsWinning ? -25 : 45 + (tablePoints * 4);
                value += candidate.Points * 0.6;
            }
            else if (partnerIsWinning)
            {
                // 队友赢着，手里有分就垫给他。
                value += 6 + (candidate.Points * 2.5);
            }
            else
            {
                // 对手赢着，别把分送出去。
                value -= candidate.Points * 4;
            }

            return value - Cost(candidate, context.Trump);
        });
    }

    private CardCombination ChooseLead(PlayContext context)
    {
        var trump = context.Trump;
        var hand = context.Hand;
        var trumpCount = hand.Count(c => IdentityResolver.Resolve(c, trump).IsTrump);
        var iAmDeclarer = context.Seat.TeamOf() == context.Info.DeclarerTeam;

        // 坐庄而且主牌多，就该先抽主牌把对面的主牌拉光。
        var drawTrumps = iAmDeclarer && trumpCount >= 7;

        return PickBest(context, candidate =>
        {
            var value = candidate.Cards.Length * 1.5;      // 一次多走几张是好事
            value -= candidate.Points * 1.6;               // 领出带分有风险
            value -= Cost(candidate, trump) * 0.6;

            if (drawTrumps)
            {
                value += candidate.IsTrump ? 22 : -8;
            }

            // 长套优先，容易做大。
            value += hand.Count(c => c.Suit == candidate.Group) * 1.2;

            return value;
        });
    }

    private CardCombination PickBest(PlayContext context, Func<CardCombination, double> score)
    {
        var best = default(CardCombination);
        var bestValue = double.NegativeInfinity;

        foreach (var candidate in context.Candidates)
        {
            // 加一点随机扰动，免得同一个局面每次都走出完全一样的牌。
            var value = score(candidate) + (rng.NextDouble() * 0.01);
            if (value > bestValue)
            {
                bestValue = value;
                best = candidate;
            }
        }

        return best ?? context.Candidates[0];
    }

    /// <summary>这一手出去能不能拿到本墩。</summary>
    private static bool WouldWin(CardCombination candidate, PlayContext context)
    {
        var trick = context.Info.CurrentTrick;
        if (trick.Count == 0)
        {
            return false;
        }

        var plays = new List<CardCombination>(trick.Count + 1);
        foreach (var play in trick)
        {
            plays.Add(play.Combo);
        }

        plays.Add(candidate);
        return TrickEvaluator.WinnerIndex(plays) == plays.Count - 1;
    }

    /// <summary>出这一手要付出多大代价。牌越大越贵，动主牌更贵。</summary>
    private static double Cost(CardCombination combo, TrumpContext trump)
    {
        var order = combo.Cards.Max(c => IdentityResolver.Resolve(c, trump).Order);
        var cost = order * 0.004;

        if (combo.IsTrump)
        {
            cost += 7;
        }

        if (combo.Type == ComboType.Tractor)
        {
            cost += 4;
        }

        return cost;
    }
}
