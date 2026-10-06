using FanKou.Core.Cards;

namespace FanKou.Core.Trump;

/// <summary>
/// 一张牌在本局里的全部"身份"。所有比大小、牌型识别都只认它，不再直接看 <see cref="Card"/>。
///
/// 三个维度是分开的，因为它们互相独立：
/// <list type="bullet">
/// <item><see cref="Suit"/> 是物理花色，只有它相同才能成对子/连对。</item>
/// <item><see cref="IsTrump"/> 决定比大小时算主牌还是副牌。</item>
/// <item><see cref="Group"/> 是跟牌时的分组（见 <see cref="FollowGroup"/>）。</item>
/// </list>
/// 主黑桃打 5 时的 ♣5：<c>Suit = 梅花</c>（能和另一张 ♣5 成对），<c>IsTrump = true</c>，
/// <c>Group = 主牌</c>（出梅花时不用给出去）。
/// </summary>
/// <param name="Role">本局角色。</param>
/// <param name="Order">牌力次序，越大越强，见 <see cref="TrumpOrder"/>。只在同一"主/副"域内比较。</param>
/// <param name="TractorRank">拖拉机专用的位次。取自然序，即级牌不提权（文档第 28 行）。</param>
/// <param name="IsTrump">是不是主牌。杀牌判定、主副对比全靠它。</param>
/// <param name="Suit">物理花色，大小王是 <see cref="Suit.Joker"/>。</param>
public readonly record struct CardIdentity(
    CardRole Role,
    int Order,
    int TractorRank,
    bool IsTrump,
    Suit Suit)
{
    /// <summary>跟牌时的分组。主牌统统归为一组，不再按原花色分。</summary>
    public FollowGroup Group => FollowGroups.Of(Suit, IsTrump);

    public bool IsJoker => Role is CardRole.SmallJoker or CardRole.BigJoker;

    /// <summary>是不是级牌（主级、副级，或小无主下的级牌）。</summary>
    public bool IsLevelCard => Role is CardRole.MainLevel or CardRole.SubLevel or CardRole.LevelCard;
}
