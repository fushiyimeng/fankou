using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 困难：会记牌、算牌，还会推算。
///
/// 在"普通"的基础上多两层：
/// <list type="number">
/// <item><b>算概率</b>：能压住我的那些牌，有一部分可能躺在没翻开的锅底里，
/// 不一定在对家手上。所以威胁要按"对家手牌数 / 没露面的牌数"折算，
/// 稳不稳看的是折算之后的期望，而不是死数。</item>
/// <item><b>推算空门</b>：谁跟牌时该给的花色没给够，谁就是这门空了。
/// 领副牌之前先看对家有没有空门等着杀我。</item>
/// </list>
/// 仍然看不到别家的手牌，也不该看得到——它只是把公开信息用得比另外几档透。
/// </summary>
public sealed class HardBrain(IRandomSource rng) : IPlayerBrain
{
    private readonly SimpleBrain _base = new(rng);

    public CallOption? ChooseCall(CallContext context) => _base.ChooseCall(context);

    public ImmutableArray<Card> ChooseBottomDiscards(BottomContext context) =>
        _base.ChooseBottomDiscards(context);

    public CardCombination ChoosePlay(PlayContext context) =>
        context.Candidates.Count == 0
            ? _base.ChoosePlay(context)
            : CountingPlayScorer.Choose(context, rng, useInference: true);
}
