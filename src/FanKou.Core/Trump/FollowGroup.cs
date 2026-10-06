using FanKou.Core.Cards;

namespace FanKou.Core.Trump;

/// <summary>
/// 跟牌时的"组"。这是比花色更高一层的概念：
/// **所有主牌归为一组**，不论它本来是哪门花色；非主牌按自己的花色分组。
///
/// 主黑桃打 5 时：♣9 属于 ♣ 组，而 ♣5 属于主牌组——
/// 所以别人出梅花时，你手上的 ♣5 根本不算梅花，不用给出去（它已经"提"成主牌了）。
/// </summary>
public enum FollowGroup : byte
{
    Spade = 0,
    Heart = 1,
    Club = 2,
    Diamond = 3,

    /// <summary>主牌组：大小王、主花色的牌、所有花色的级牌。</summary>
    Trump = 4,
}

public static class FollowGroups
{
    public static FollowGroup Of(Suit suit, bool isTrump) =>
        isTrump ? FollowGroup.Trump : (FollowGroup)(byte)suit;

    /// <summary>给玩家看的名字。</summary>
    public static string Text(this FollowGroup group) =>
        group == FollowGroup.Trump ? "主牌" : ((Suit)(byte)group).Symbol();
}
