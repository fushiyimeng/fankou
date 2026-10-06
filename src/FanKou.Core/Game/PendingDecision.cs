using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;

namespace FanKou.Core.Game;

/// <summary>
/// "现在轮到谁做什么"。界面和 AI 都只通过它和引擎打交道——
/// 所以同一个引擎可以脱离界面跑上万局。
/// </summary>
public abstract record PendingDecision(Seat Actor)
{
    /// <param name="IsCounter">
    /// true 表示这是**反主**的机会（对手用更高规格把主牌抢过去），不是普通叫主。
    /// 反主永远是"想反才反"，所以 CanPass 一定是 true。
    /// </param>
    public sealed record NeedCall(
        Seat Actor,
        ImmutableArray<Card> Hand,
        IReadOnlyList<CallOption> Options,
        bool CanPass,
        Rank LevelRank,
        bool IsCounter = false) : PendingDecision(Actor);

    /// <param name="Hand">叫主之前手上的 25 张。</param>
    /// <param name="Bottom">锅底那 8 张，也要一起拿上来挑。</param>
    /// <param name="Locked">叫主摆出来的明牌，不能扣。</param>
    public sealed record NeedBottomSwap(
        Seat Actor,
        ImmutableArray<Card> Hand,
        ImmutableArray<Card> Bottom,
        ImmutableArray<Card> Locked) : PendingDecision(Actor)
    {
        /// <summary>合起来 33 张——这才是真正能挑的范围。</summary>
        public ImmutableArray<Card> Pool => [.. Hand, .. Bottom];
    }

    /// <param name="Lead">领出的那一手；自己是领出方时为 null。</param>
    /// <param name="Candidates">已经过合法性校验的候选出牌。</param>
    public sealed record NeedPlay(
        Seat Actor,
        ImmutableArray<Card> Hand,
        CardCombination? Lead,
        IReadOnlyList<CardCombination> Candidates)
        : PendingDecision(Actor);
}
