using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 普通：会记牌、算牌。
///
/// 比"简单"档多知道的是：**还没露过面的牌有哪些**。
/// 于是它判断"我这一手能不能赢"不再是凭感觉，而是数一数外面还剩几手能压它——
/// 剩 0 手就是稳的，值得动大牌；剩好几手就得掂量。
/// 叫主和扣底牌沿用简单档的逻辑（那两块记不记牌差别不大）。
/// </summary>
public sealed class NormalBrain(IRandomSource rng) : IPlayerBrain
{
    private readonly SimpleBrain _base = new(rng);

    public CallOption? ChooseCall(CallContext context) => _base.ChooseCall(context);

    public ImmutableArray<Card> ChooseBottomDiscards(BottomContext context) =>
        _base.ChooseBottomDiscards(context);

    public CardCombination ChoosePlay(PlayContext context) =>
        context.Candidates.Count == 0
            ? _base.ChoosePlay(context)
            : CountingPlayScorer.Choose(context, rng, useInference: false);
}
