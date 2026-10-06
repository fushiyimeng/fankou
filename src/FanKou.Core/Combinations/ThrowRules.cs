using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Combinations;

/// <summary>
/// 甩牌的合法性。文档第 27 行：
/// "散牌甩牌，是必须同花色。且没有人比你大牌大，比如黑桃 AKQ，
/// 那么场上所有人手里没有大于黑桃 Q 的就可以出牌，反正则无法出牌
/// （线下有惩罚机制，线上的话，让系统监管不让出就可以了）。"
///
/// 所以这里只做一件事：看看**别家手上**有没有同一门里更大的牌。
/// 判据只看同花色——别人拿主牌来杀那是出牌时候的事，不影响甩牌本身成不成立。
/// 已经打出去的牌压不到你，锅底里的牌也不在人手上，所以都不算。
/// </summary>
public static class ThrowRules
{
    /// <param name="otherHandCards">**别家手上**的所有牌（不含锅底）。</param>
    public static bool IsLegal(
        CardCombination combo,
        IEnumerable<Card> otherHandCards,
        TrumpContext trump) =>
        IsLegal(combo, MaxOrderByGroup(otherHandCards, trump), trump);

    /// <summary>
    /// 别家手上每一门里最大的牌力。生成候选的时候先算一次，
    /// 之后每个候选只比一次就行——不然每试一个甩牌都要把别家的牌扫一遍，慢得离谱。
    /// </summary>
    public static Dictionary<FollowGroup, int> MaxOrderByGroup(
        IEnumerable<Card> otherHandCards,
        TrumpContext trump)
    {
        var result = new Dictionary<FollowGroup, int>();

        foreach (var card in otherHandCards)
        {
            var identity = IdentityResolver.Resolve(card, trump);

            if (!result.TryGetValue(identity.Group, out var current) || identity.Order > current)
            {
                result[identity.Group] = identity.Order;
            }
        }

        return result;
    }

    public static bool IsLegal(
        CardCombination combo,
        IReadOnlyDictionary<FollowGroup, int> othersMaxOrder,
        TrumpContext trump)
    {
        if (combo.Type != ComboType.Throw)
        {
            return true;
        }

        // 按**跟牌分组**比，不是按花色比——打 K 的时候 ♣K 是主牌，它根本不算梅花，
        // 挡不住 ♣A♣A♣Q。这正是 AAK 和 AAQ 的由来。
        var group = IdentityResolver.Resolve(combo.Cards[0], trump).Group;

        if (!othersMaxOrder.TryGetValue(group, out var theirBest))
        {
            return true;   // 别人这门一张都没有，随便甩
        }

        // 只要没人压得住这手牌里**最小**的那张，就没人压得住整手。
        var mine = combo.Cards.Min(card => IdentityResolver.Resolve(card, trump).Order);

        return theirBest <= mine;
    }
}
