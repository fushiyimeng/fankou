using FanKou.Core.Trump;

namespace FanKou.Core.Cards;

/// <summary>
/// 手牌摆出来的顺序：主牌排在最前面，然后是各门副牌，每门里面从大到小。
/// 放在 Core 里是为了能单独测——顺序错了玩家很难受，但又不容易一眼看出来。
/// </summary>
public static class CardSorter
{
    public static IReadOnlyList<Card> ForDisplay(IEnumerable<Card> cards, TrumpContext? trump)
    {
        if (trump is null)
        {
            return [.. cards.OrderBy(c => (int)c.Suit).ThenByDescending(c => (int)c.Rank)];
        }

        return
        [
            .. cards
                .OrderBy(c => GroupRank(c, trump))
                .ThenByDescending(c => IdentityResolver.Resolve(c, trump).Order)
                .ThenBy(c => c.Copy),
        ];
    }

    /// <summary>主牌永远排第一档，剩下的按花色。注意不能直接用 FollowGroup 的枚举值当序号。</summary>
    public static int GroupRank(Card card, TrumpContext trump) =>
        IdentityResolver.Resolve(card, trump).Group switch
        {
            FollowGroup.Trump => 0,
            FollowGroup.Spade => 1,
            FollowGroup.Heart => 2,
            FollowGroup.Club => 3,
            FollowGroup.Diamond => 4,
            _ => 5,
        };
}
