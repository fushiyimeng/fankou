using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Trump;

namespace FanKou.Core.Play;

/// <summary>一次跟牌的全部输入。规则只读它，不修改任何东西。</summary>
public sealed record FollowContext(
    CardCombination Lead,
    ImmutableArray<Card> Hand,
    ImmutableArray<Card> Selected,
    TrumpContext Trump)
{
    public RuleOptions Options => Trump.Options;

    /// <summary>
    /// 领出的这一手属于哪个跟牌组。领出主牌时，**所有**主牌都算"同一门"，
    /// 所以主黑桃打 5 时，出 ♠9 和出 ♥5 是同一门牌（都得给主牌）。
    /// </summary>
    public FollowGroup LeadGroup => LeadGroupOf(Lead);

    public string LeadGroupName => LeadGroup.Text();

    public bool BelongsToLeadGroup(Card card) => BelongsToLeadGroup(card, Lead, Trump);

    /// <summary>手上属于领出这一组的牌。</summary>
    public Card[] HandOfLeadGroup => [.. Hand.Where(BelongsToLeadGroup)];

    public Card[] SelectedOfLeadGroup => [.. Selected.Where(BelongsToLeadGroup)];

    public bool IsTrump(Card card) => IdentityResolver.Resolve(card, Trump).IsTrump;

    public static FollowGroup LeadGroupOf(CardCombination lead) =>
        lead.IsTrump ? FollowGroup.Trump : FollowGroups.Of(lead.Group, false);

    /// <summary>
    /// 这张牌在"给牌"时算不算领出的那一组。
    /// 关键是：**副级牌算主牌，不算它原来的花色**——
    /// 主黑桃打 5 时的 ♣5 是副主，别人出梅花时不用给出去。
    /// </summary>
    public static bool BelongsToLeadGroup(Card card, CardCombination lead, TrumpContext context) =>
        IdentityResolver.Resolve(card, context).Group == LeadGroupOf(lead);
}
