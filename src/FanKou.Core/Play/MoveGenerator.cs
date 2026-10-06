using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Play.Rules;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Core.Play;

/// <summary>
/// 枚举合法出牌，给界面"提示"按钮和 AI 用。
///
/// 生成的每一手都会过一遍 <see cref="MoveValidator"/>，所以**保证合法**（不会给出走不出去的牌）。
/// 但对于"手上 25 张、要垫 8 张"这种组合数爆炸的局面会截断，
/// 因此不保证穷尽——玩家仍可手选任意合法牌，界面拿 MoveValidator 单独判。
/// </summary>
public static class MoveGenerator
{
    private const int FillerCap = 400;
    private const int CoreCap = 200;

    /// <summary>甩牌最多试到几张。再多就只试"这门全甩"那一种，免得候选爆炸。</summary>
    private const int MaxThrowProbe = 6;

    /// <param name="otherHandCards">
    /// 别家手上的牌（不含锅底）。只有甩牌要用它判合不合法——
    /// 传 null 就不生成甩牌，因为判不了合法性，生成出来也是害人。
    /// </param>
    public static IReadOnlyList<CardCombination> Generate(
        CardCombination? lead,
        ImmutableArray<Card> hand,
        TrumpContext context,
        IReadOnlyList<Card>? otherHandCards = null) =>
        lead is null
            ? GenerateLeads(hand, context, otherHandCards)
            : GenerateFollows(lead, hand, context);

    public static IReadOnlyList<CardCombination> GenerateLeads(
        ImmutableArray<Card> hand,
        TrumpContext context,
        IReadOnlyList<Card>? otherHandCards = null)
    {
        var results = new List<CardCombination>();
        var seen = new HashSet<string>();

        void Add(IEnumerable<Card> cards)
        {
            var list = cards.ToArray();
            if (list.Length == 0 || !ComboRecognizer.TryRecognize(list, context, out var combo))
            {
                return;
            }

            if (seen.Add(CardSet.ShapeKey(list)))
            {
                results.Add(combo!);
            }
        }

        // 单张
        foreach (var card in CardSet.DistinctByShape(hand))
        {
            Add([card]);
        }

        // 对子（两张王也算，花色和点数都相同，认牌型时自然成立）
        foreach (var group in hand.GroupBy(c => (c.Suit, c.Rank)).Where(g => g.Count() >= 2))
        {
            Add(group.Take(2));
        }

        // 甩牌：同花色的散牌（AKQ、AAK 这类）。
        // 只试"这门最大的几张"——甩牌合法要求最小的那张都没人压得住，
        // 所以能甩出去的必然是从这门顶端往下数的几张。
        // 别家每门最大牌力先算一次，后面每个甩牌候选只比一次。
        var othersMax = otherHandCards is null
            ? []
            : ThrowRules.MaxOrderByGroup(otherHandCards, context);

        if (otherHandCards is not null && context.Options.AllowThrows)
        {
            // 按跟牌分组来分，不是按花色——级牌算主牌，不在副花色里。
            // 主牌那一组跳过：一手全是主牌的不能当甩牌出（那是杀牌的事）。
            foreach (var group in hand.GroupBy(c => IdentityResolver.Resolve(c, context).Group))
            {
                if (group.Key == FollowGroup.Trump)
                {
                    continue;
                }

                var ofGroup = group
                    .OrderByDescending(c => IdentityResolver.Resolve(c, context).Order)
                    .ToArray();

                if (ofGroup.Length < 3)
                {
                    continue;
                }

                var limit = Math.Min(ofGroup.Length, MaxThrowProbe);

                for (var size = 3; size <= limit; size++)
                {
                    AddThrow([.. ofGroup.Take(size)]);
                }

                if (ofGroup.Length > MaxThrowProbe)
                {
                    AddThrow(ofGroup);
                }
            }
        }

        // 拖拉机：同花色里所有长度 >= 2 的连续对子段
        foreach (var suitGroup in hand.GroupBy(c => c.Suit))
        {
            var pairs = suitGroup
                .GroupBy(c => c.Rank)
                .Where(g => g.Count() >= 2)
                .Select(g => (Rank: g.Key, Cards: g.Take(2).ToArray(), Order: TractorOrder(g.First(), context)))
                .OrderBy(x => x.Order)
                .ToArray();

            for (var start = 0; start < pairs.Length; start++)
            {
                var run = new List<Card>();
                for (var end = start; end < pairs.Length; end++)
                {
                    if (end > start && pairs[end].Order != pairs[end - 1].Order + 1)
                    {
                        break;
                    }

                    run.AddRange(pairs[end].Cards);
                    if (end > start)
                    {
                        Add(run);
                    }
                }
            }
        }

        return results;

        void AddThrow(Card[] cards)
        {
            if (!ComboRecognizer.TryRecognize(cards, context, out var candidate)
                || candidate!.Type != ComboType.Throw)
            {
                // 认成了对子/拖拉机也没关系，前面那些循环已经把它们加过了
                return;
            }

            if (!ThrowRules.IsLegal(candidate, othersMax, context))
            {
                return;
            }

            if (seen.Add(CardSet.ShapeKey(cards)))
            {
                results.Add(candidate);
            }
        }
    }

    public static IReadOnlyList<CardCombination> GenerateFollows(
        CardCombination lead,
        ImmutableArray<Card> hand,
        TrumpContext context)
    {
        var results = new List<CardCombination>();
        var seen = new HashSet<string>();
        var total = lead.Length;

        if (hand.Length < total)
        {
            return results;
        }

        var handOfGroup = hand.Where(c => FollowContext.BelongsToLeadGroup(c, lead, context)).ToArray();
        var mustPlayOfGroup = Math.Min(handOfGroup.Length, total);

        void TryAdd(IEnumerable<Card> cards)
        {
            var list = cards.ToArray();
            if (list.Length != total || !CardSet.ContainsAll(hand, list) || !seen.Add(CardSet.ShapeKey(list)))
            {
                return;
            }

            if (!MoveValidator.Validate(lead, hand, [.. list], context).IsValid)
            {
                return;
            }

            results.Add(ComboRecognizer.TryRecognize(list, context, out var combo)
                ? combo!
                : CardCombination.Shapeless(list, list.All(c => IsTrump(c, context)), lead.Group));
        }

        foreach (var core in GenerateCores(lead, handOfGroup, mustPlayOfGroup, context))
        {
            var gap = total - core.Length;
            if (gap == 0)
            {
                TryAdd(core);
                continue;
            }

            var remaining = CardSet.Remove(hand, core);
            foreach (var filler in FillerChoices(lead, remaining, handOfGroup.Length > 0, gap, context))
            {
                TryAdd([.. core, .. filler]);
            }
        }

        return results;
    }

    private static IEnumerable<Card[]> GenerateCores(
        CardCombination lead,
        Card[] handOfGroup,
        int mustPlay,
        TrumpContext context)
    {
        if (mustPlay == 0)
        {
            yield return [];
            yield break;
        }

        var distinct = CardSet.DistinctByShape(handOfGroup).ToArray();

        switch (lead.Type)
        {
            case ComboType.Single:
                foreach (var card in distinct)
                {
                    yield return [card];
                }

                break;

            case ComboType.Pair:
                if (mustPlay == 1)
                {
                    foreach (var card in distinct)
                    {
                        yield return [card];
                    }

                    break;
                }

                // 按 (花色, 点数) 分组——领出主牌时跟牌组里混着各门花色，
                // ♠7 和 ♥7 不是一对。
                var pairedGroups = handOfGroup.GroupBy(c => (c.Suit, c.Rank)).Where(g => g.Count() >= 2).ToArray();
                if (pairedGroups.Length > 0)
                {
                    // 有对子就必须出对子，出哪一对随意。
                    foreach (var group in pairedGroups)
                    {
                        yield return [.. group.Take(2)];
                    }
                }
                else
                {
                    foreach (var cards in Combos.Of(distinct, 2, CoreCap))
                    {
                        yield return cards;
                    }
                }

                break;

            case ComboType.Tractor:
                foreach (var core in TractorCores(lead, handOfGroup, mustPlay, context))
                {
                    yield return core;
                }

                break;

            default:
                // 甩牌（顶牌）这类散牌领出：随便出够张数就行。
                // 注意这里不能用 DistinctByShape——两副牌里有两张一模一样的牌，
                // 而跟牌恰恰可能要求把两张都出掉。
                foreach (var cards in Combos.Of(DistinctByShape(mustPlay, handOfGroup), mustPlay, CoreCap))
                {
                    yield return cards;
                }

                break;
        }
    }

    /// <summary>
    /// 按花色点数去重，但每种最多留 <paramref name="maxCopies"/> 份。
    /// 直接 DistinctByShape 会把"两张一模一样的牌"压成一张，
    /// 而跟牌经常需要把两张同名牌一起出（比如手上就一对 ♣9 要跟三张的牌）。
    /// </summary>
    private static Card[] DistinctByShape(int maxCopies, IEnumerable<Card> cards)
    {
        var keep = Math.Max(1, maxCopies);

        return
        [
            .. cards
                .GroupBy(c => (c.Suit, c.Rank))
                .SelectMany(group => group.Take(keep)),
        ];
    }

    private static IEnumerable<Card[]> TractorCores(
        CardCombination lead,
        Card[] handOfGroup,
        int mustPlay,
        TrumpContext context)
    {
        var needPairs = FollowStructure.NeededPairs(handOfGroup, lead.PairCount, mustPlay);

        // 对子按 (花色, 点数) 分组：领出主牌时跟牌组里各门花色都有。
        var pairGroups = handOfGroup.GroupBy(c => (c.Suit, c.Rank)).Where(g => g.Count() >= 2).ToArray();

        List<Card[][]> pairOptions;

        if (needPairs == 0)
        {
            pairOptions = [[]];
        }
        else if (MustKeepRun(handOfGroup, needPairs, context))
        {
            // 手上有连对，出的对子也必须连着。连对还得同一花色，所以按花色分开找窗口。
            pairOptions = [];
            foreach (var suitGroup in pairGroups.GroupBy(g => g.Key.Suit))
            {
                pairOptions.AddRange(ConsecutiveWindows([.. suitGroup], needPairs, context));
            }
        }
        else
        {
            var candidates = pairGroups.Select(g => g.Take(2).ToArray()).ToArray();
            pairOptions = [.. Combos.Of(candidates, needPairs, CoreCap)];
        }

        foreach (var pairs in pairOptions)
        {
            var core = pairs.SelectMany(p => p).ToArray();
            var singlesNeeded = mustPlay - core.Length;

            if (singlesNeeded <= 0)
            {
                yield return core;
                continue;
            }

            var rest = CardSet.DistinctByShape(CardSet.Remove(handOfGroup, core)).ToArray();
            foreach (var singles in Combos.Of(rest, singlesNeeded, CoreCap))
            {
                yield return [.. core, .. singles];
            }
        }
    }

    /// <summary>手上存在够长的连对，且规则要求保持连对形状。</summary>
    private static bool MustKeepRun(Card[] handOfGroup, int needPairs, TrumpContext context) =>
        needPairs >= 2
        && context.Options.TractorFollowPolicy == TractorPolicy.StructurePreserving
        && FollowStructure.LongestRun(handOfGroup, context) >= needPairs;

    /// <summary>同一花色里所有长度为 <paramref name="need"/> 的连续对子窗口。</summary>
    private static List<Card[][]> ConsecutiveWindows(
        IGrouping<(Suit Suit, Rank Rank), Card>[] pairGroups,
        int need,
        TrumpContext context)
    {
        var ordered = pairGroups
            .Select(g => (Cards: g.Take(2).ToArray(), Order: TractorOrder(g.First(), context)))
            .OrderBy(x => x.Order)
            .ToArray();

        var windows = new List<Card[][]>();
        for (var start = 0; start + need <= ordered.Length; start++)
        {
            var consecutive = true;
            for (var i = start; i + 1 < start + need; i++)
            {
                if (ordered[i + 1].Order != ordered[i].Order + 1)
                {
                    consecutive = false;
                    break;
                }
            }

            if (consecutive)
            {
                windows.Add([.. ordered.Skip(start).Take(need).Select(x => x.Cards)]);
            }
        }

        return windows;
    }

    private static IEnumerable<Card[]> FillerChoices(
        CardCombination lead,
        ImmutableArray<Card> remaining,
        bool hasLeadGroup,
        int gap,
        TrumpContext context)
    {
        IEnumerable<Card> pool = remaining;

        if (lead.Type is ComboType.Pair or ComboType.Tractor
            && TrumpFillerRule.MustFillWithTrump(context.Options.PairFillerPolicy, hasLeadGroup))
        {
            // 策略要求垫主牌。主牌够就只用主牌；不够就把其余的也放进去——
            // 校验器只要求尽量垫，不会因为凑不齐就把整手放行。
            var trump = remaining.Where(c => IsTrump(c, context)).ToArray();
            pool = trump.Length >= gap ? trump : [.. trump, .. remaining.Where(c => !IsTrump(c, context))];
        }

        return Combos.Of([.. pool], gap, FillerCap);
    }

    private static bool IsTrump(Card card, TrumpContext context) =>
        IdentityResolver.Resolve(card, context).IsTrump;

    /// <summary>拖拉机的位次。连对相邻性和大小都用它，不是牌力次序。</summary>
    private static int TractorOrder(Card card, TrumpContext context) =>
        IdentityResolver.Resolve(card, context).TractorRank;
}
