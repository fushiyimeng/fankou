using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Combinations;

/// <summary>
/// 把一组牌认成"单张 / 对子 / 拖拉机"。认不出来就返回 false 并给出中文原因。
/// </summary>
public static class ComboRecognizer
{
    public static bool TryRecognize(
        IReadOnlyList<Card> cards,
        TrumpContext context,
        out CardCombination? combo,
        out string reason)
    {
        combo = null;

        if (cards.Count == 0)
        {
            reason = "没有出牌";
            return false;
        }

        if (cards.Count == 1)
        {
            var identity = IdentityResolver.Resolve(cards[0], context);
            combo = CardCombination.Create(
                ComboType.Single, cards, cards[0].Suit, identity.IsTrump, pairCount: 0, key: identity.Order);
            reason = string.Empty;
            return true;
        }

        if (cards.Count == 2)
        {
            return TryRecognizePair(cards, context, out combo, out reason);
        }

        // 四张及以上偶数张：先试拖拉机，不成再看是不是甩牌（比如 AKQJ）。
        if (cards.Count % 2 == 0 && cards.Count >= 4)
        {
            if (TryRecognizeTractor(cards, context, out combo, out var tractorReason))
            {
                reason = string.Empty;
                return true;
            }

            if (TryRecognizeThrow(cards, context, out combo, out var throwReason))
            {
                reason = string.Empty;
                return true;
            }

            reason = $"这 {cards.Count} 张既不是拖拉机（{tractorReason}），也不是甩牌（{throwReason}）";
            return false;
        }

        // 三张：只能是甩牌（AKQ / AAK 这类）。
        if (cards.Count == 3)
        {
            return TryRecognizeThrow(cards, context, out combo, out reason);
        }

        reason = $"不支持出 {cards.Count} 张牌";
        return false;
    }

    /// <summary>
    /// 这门牌在当前局里的"最大"和"次大"两个点数。
    /// 打 K 的时候 K 是主牌、已经从副花色里拿掉了，所以副花色的次大是 Q 而不是 K——
    /// 这就是 AAK 和 AAQ 的由来。
    /// </summary>
    public static (Rank Top, Rank Next) TopTripleRanks(Suit suit, TrumpContext context)
    {
        // 主花色里的牌全是主牌，一个都不排除；副花色要把已经提成主牌的那些摘掉
        // （打 5 摘 5、打 K 摘 K；大无主下级牌不再是主牌，就一个都不摘）。
        var ranks = Ranks.Standard
            .Where(rank => suit == context.TrumpSuit
                || !IdentityResolver.Resolve(new Card(suit, rank, 0), context).IsTrump)
            .OrderByDescending(rank => rank)
            .ToArray();

        return (ranks[0], ranks[1]);
    }

    /// <summary>
    /// 甩牌：同花色的几张散牌一起出。
    ///
    /// **这里只管认形状**（同花色、三张以上、不是对子也不是拖拉机），
    /// 合不合法（这门里别人手上有没有更大的牌）要到真正出牌的时候由
    /// <see cref="ThrowRules.IsLegal"/> 判——那需要看到别家的牌，
    /// 不是认牌型这一层能知道的事。
    /// </summary>
    private static bool TryRecognizeThrow(
        IReadOnlyList<Card> cards,
        TrumpContext context,
        out CardCombination? combo,
        out string reason)
    {
        combo = null;

        if (!context.Options.AllowThrows)
        {
            reason = "按当前规则甩牌不开放（可在 rules.json 里打开）";
            return false;
        }

        var suits = cards.Select(c => c.Suit).Distinct().ToArray();
        if (suits.Length != 1 || suits[0] == Suit.Joker)
        {
            reason = "甩牌必须同一花色";
            return false;
        }

        var identities = IdentityResolver.ResolveAll(cards, context);

        // 一手甩牌里的牌必须同属一门：要么全是副牌，要么全是主牌。
        // 混着来就两头不靠——比如主黑桃打 5 时的 ♣5♣5♣6♣6，♣5 是主牌、♣6 是梅花。
        if (identities.Select(i => i.Group).Distinct().Count() > 1)
        {
            reason = "一手甩牌里的牌必须同属一门（级牌算主牌，不算原来的花色）";
            return false;
        }

        combo = CardCombination.Create(
            ComboType.Throw,
            cards,
            suits[0],
            identities.All(i => i.IsTrump),
            pairCount: 0,
            key: identities.Max(i => i.Order));

        reason = string.Empty;
        return true;
    }

    public static bool TryRecognize(
        IReadOnlyList<Card> cards,
        TrumpContext context,
        out CardCombination? combo) =>
        TryRecognize(cards, context, out combo, out _);

    private static bool TryRecognizePair(
        IReadOnlyList<Card> cards,
        TrumpContext context,
        out CardCombination? combo,
        out string reason)
    {
        combo = null;
        var first = cards[0];
        var second = cards[1];

        // 同花色同点数才是对子。两张小王（或两张大王）花色都是 Joker、点数也相同，
        // 所以"王对也算对子"（文档第 24 行）不需要特判，自然成立。
        if (first.Suit != second.Suit || first.Rank != second.Rank)
        {
            reason = $"{CardText.Format(first)} 和 {CardText.Format(second)} 不是对子（必须同花色同点数）";
            return false;
        }

        var identity = IdentityResolver.Resolve(first, context);
        combo = CardCombination.Create(
            ComboType.Pair, cards, first.Suit, identity.IsTrump, pairCount: 1, key: identity.Order);
        reason = string.Empty;
        return true;
    }

    private static bool TryRecognizeTractor(
        IReadOnlyList<Card> cards,
        TrumpContext context,
        out CardCombination? combo,
        out string reason)
    {
        combo = null;

        var groups = cards.Select(c => c.Suit).Distinct().ToArray();
        if (groups.Length != 1)
        {
            reason = "拖拉机必须同一花色";
            return false;
        }

        var group = groups[0];

        // 每个点数必须恰好两张，多一张少一张都不成对。
        var byRank = cards.GroupBy(c => c.Rank).ToArray();
        if (byRank.Any(g => g.Count() != 2))
        {
            reason = "拖拉机要求每个点数恰好两张";
            return false;
        }

        var pairs = ImmutableArray.CreateBuilder<(int TractorRank, Card Card)>(byRank.Length);
        foreach (var g in byRank)
        {
            var identity = IdentityResolver.Resolve(g.First(), context);
            pairs.Add((identity.TractorRank, g.First()));
        }

        var ordered = pairs.OrderBy(p => p.TractorRank).ToArray();

        for (var i = 0; i + 1 < ordered.Length; i++)
        {
            if (ordered[i + 1].TractorRank != ordered[i].TractorRank + 1)
            {
                reason = "拖拉机的对子必须挨着（连对）";
                return false;
            }
        }

        var identities = IdentityResolver.ResolveAll(cards, context);
        var isTrump = identities.All(i => i.IsTrump);
        var pairCount = ordered.Length;
        var key = ordered[^1].TractorRank;

        if (group == Suit.Joker && !context.Options.JokerPairChainIsTractor)
        {
            // 文档第 65 行说"两对王可以租拖拉机"，第 89 行用户又标注待问长辈。
            // 开关默认开着；关掉后王对只能当两对分别出。
            reason = "两张小王 + 两张大王按当前规则不算连对（可在 rules.json 里打开）";
            return false;
        }

        // 连对里的牌必须落在同一个跟牌组：要么全是主牌，要么全是同一门副牌。
        // 主黑桃打 5 时的 ♣4455 不算连对——♣5 是副主，它压根不算梅花，
        // 所以这一手是"一张主牌夹在副花色里"，两头不靠。
        if (identities.Select(i => i.Group).Distinct().Count() > 1)
        {
            reason = "这组牌里混了主牌和副牌（级牌算主牌，不算原来的花色），成不了连对";
            return false;
        }

        combo = CardCombination.Create(ComboType.Tractor, cards, group, isTrump, pairCount, key);
        reason = string.Empty;
        return true;
    }
}
