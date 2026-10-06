using FanKou.Core.Cards;

namespace FanKou.Core.Trump;

/// <summary>
/// "这张牌在本局算老几"的**唯一**真相源。任何模块都不许再自己写一套判断。
/// </summary>
public static class IdentityResolver
{
    public static CardIdentity Resolve(Card card, TrumpContext context)
    {
        var options = context.Options;
        var natural = card.Rank.NaturalIndex();

        if (card.IsBigJoker)
        {
            return new CardIdentity(CardRole.BigJoker, TrumpOrder.BigJoker, natural, true, Suit.Joker);
        }

        if (card.IsSmallJoker)
        {
            return new CardIdentity(CardRole.SmallJoker, TrumpOrder.SmallJoker, natural, true, Suit.Joker);
        }

        var isLevel = context.LevelRanks.Contains(card.Rank);

        var (role, order, isTrump) = context.Kind switch
        {
            // 叫主 / 定主：两者产生的主牌上下文完全一样
            CallKind.SuitedSuit or CallKind.SuitedBoth => ResolveSuited(card, context, isLevel, natural),

            // 小无主：所有花色的级牌同为一级，不分主副（文档第 64 行）
            CallKind.SmallNoTrump => isLevel
                ? (CardRole.LevelCard, TrumpOrder.SubLevel, true)
                : (CardRole.OffSuit, TrumpOrder.OffSuitBase + natural, false),

            // 大无主：只有大小王是主，级牌不再是主（文档第 65 行"5 已经不是主了"）
            CallKind.BigNoTrump => (CardRole.OffSuit, TrumpOrder.OffSuitBase + natural, false),

            _ => throw new NotSupportedException($"未知的叫主规格：{context.Kind}"),
        };

        // 拖拉机默认按自然序（级牌不提权，文档第 28 行）。
        // TractorUsesNaturalOrder = false 是留给"长辈说主级牌在拖拉机里也最大"的逃生口，默认走不到。
        var tractorRank = options.TractorUsesNaturalOrder ? natural : order;

        return new CardIdentity(role, order, tractorRank, isTrump, card.Suit);
    }

    private static (CardRole Role, int Order, bool IsTrump) ResolveSuited(
        Card card, TrumpContext context, bool isLevel, int natural)
    {
        if (card.Suit == context.TrumpSuit)
        {
            return isLevel
                ? (CardRole.MainLevel, TrumpOrder.MainLevel, true)
                : (CardRole.TrumpSuit, TrumpOrder.TrumpSuitBase + natural, true);
        }

        // 副级牌本身是主牌（文档第 63 行"所有其它花色 5 都是副主"），
        // 且排在主花色 AKQJ…2 之前（文档第 62 行）。
        return isLevel
            ? (CardRole.SubLevel, TrumpOrder.SubLevel, true)
            : (CardRole.OffSuit, TrumpOrder.OffSuitBase + natural, false);
    }

    public static CardIdentity[] ResolveAll(IEnumerable<Card> cards, TrumpContext context) =>
        [.. cards.Select(c => Resolve(c, context))];
}
