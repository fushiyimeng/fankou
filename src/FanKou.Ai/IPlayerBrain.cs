using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Trump;

namespace FanKou.Ai;

/// <summary>
/// AI 和规则引擎之间的唯一边界。AI 只能看到公开信息，
/// 别家的手牌在类型上就拿不到，不靠自觉。
/// </summary>
public interface IPlayerBrain
{
    /// <summary>返回 null 表示不叫（只有在 <see cref="CallContext.CanPass"/> 时才允许）。</summary>
    CallOption? ChooseCall(CallContext context);

    /// <summary>扣 8 张进底牌。明牌不能扣。</summary>
    ImmutableArray<Card> ChooseBottomDiscards(BottomContext context);

    /// <summary>从已经过合法性校验的候选里挑一手。</summary>
    CardCombination ChoosePlay(PlayContext context);
}

public sealed record CallContext(
    Seat Seat,
    ImmutableArray<Card> Hand,
    IReadOnlyList<CallOption> Options,
    bool CanPass,
    Rank LevelRank,
    bool IsCounter = false);

public sealed record BottomContext(
    Seat Seat,
    ImmutableArray<Card> Hand,
    ImmutableArray<Card> Locked,
    TrumpContext Trump);

/// <summary>AI 能看到的公开信息。**不含**任何别家的手牌。</summary>
public sealed record PublicBoardInfo(
    IReadOnlyList<int> RemainingCounts,
    IReadOnlyList<TrickPlay> CurrentTrick,
    IReadOnlyList<TrickRecord> CompletedTricks,
    IReadOnlyList<Card> ShownCards,
    Team DeclarerTeam,
    Seat DeclarerSeat,
    int DefenderTrickPoints,
    int DeclarerTrickPoints,
    int TrickNumber)
{
    /// <summary>
    /// 锅底那 8 张——**只有庄家拿得到**，别人一律是空的。
    ///
    /// 这不是偷看：那 8 张本来就是庄家自己挑着扣下去的，规则上他知道。
    /// 之前这里一律不传，等于**让 AI 把自己埋的牌忘了**，很多推算做不了
    /// （比如"我扣掉多少分就锁定胜局了"）。
    /// 真正该藏的只有别家手牌。
    /// </summary>
    public IReadOnlyList<Card> Bottom { get; init; } = [];

    public bool TrickIsEmpty => CurrentTrick.Count == 0;

    /// <summary>本墩目前是谁最大（按出牌顺序），没人出牌时为 null。</summary>
    public Seat? CurrentTrickWinner =>
        CurrentTrick.Count == 0
            ? null
            : CurrentTrick[TrickEvaluatorIndex(CurrentTrick)].Seat;

    /// <summary>已经出过面（打过或者叫主亮过）的所有牌。AI 的"记牌"就是记这个。</summary>
    public IEnumerable<Card> SeenCards()
    {
        foreach (var card in ShownCards)
        {
            yield return card;
        }

        foreach (var trick in CompletedTricks)
        {
            foreach (var play in trick.Plays)
            {
                foreach (var card in play.Combo.Cards)
                {
                    yield return card;
                }
            }
        }

        foreach (var play in CurrentTrick)
        {
            foreach (var card in play.Combo.Cards)
            {
                yield return card;
            }
        }
    }

    private static int TrickEvaluatorIndex(IReadOnlyList<TrickPlay> plays)
    {
        var combos = new List<CardCombination>(plays.Count);
        foreach (var play in plays)
        {
            combos.Add(play.Combo);
        }

        return Core.Play.TrickEvaluator.WinnerIndex(combos);
    }
}

public sealed record PlayContext(
    Seat Seat,
    ImmutableArray<Card> Hand,
    CardCombination? Lead,
    IReadOnlyList<CardCombination> Candidates,
    TrumpContext Trump,
    PublicBoardInfo Info)
{
    public bool IsLeading => Lead is null;
}
