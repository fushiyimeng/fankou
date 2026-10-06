using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Trump;

namespace FanKou.Core.Game;

/// <summary>
/// <see cref="GameEngine"/> 某一个瞬间的全部可变状态，给「撤回」用。
///
/// **字段顺序跟 <c>GameEngine</c> 里的字段声明区一一对应**，加字段时两边一起加——
/// 漏一个编译器不会报错，只会在撤回之后表现为某个数字悄悄不对（比如分数还回去了但
/// <see cref="Leader"/> 没还），很难查。<c>UndoTests</c> 里那条"还原后重放同样的命令、
/// 结果必须和第一次完全一致"就是拿来逼这种漏项的。
///
/// 这里存的都是不可变类型的快照（<see cref="Card"/> 是 readonly record struct，
/// 其余是 record 或者已经展开成 <see cref="ImmutableArray{T}"/>），拿到之后不会再被引擎改到。
///
/// **不包含**规则开关（<see cref="Config.RuleOptions"/> 是"下一局生效"的配置，不是这一局的进度）
/// 和随机源（<c>IRandomSource</c> 没暴露内部状态，存不了）。
/// </summary>
public sealed record GameEngineState(
    ImmutableArray<ImmutableArray<Card>> Hands,
    ImmutableArray<Card> Bottom,
    ImmutableArray<Card> Locked,
    ImmutableArray<Card> Deck,
    int DealCursor,
    Seat DealStart,
    bool SlowDeal,
    bool CalledDuringDeal,
    CallOption? AcceptedOption,
    ImmutableArray<int> Levels,
    ImmutableArray<int> LevelsAtRoundStart,
    Team DeclarerTeam,
    bool HasDeclarer,
    int RoundNumber,
    Seat DeclarerSeat,
    TrumpContext? Trump,
    Rank LevelRank,
    ImmutableArray<Seat> CallQueue,
    int CallIndex,
    CallRound CallRound,
    ImmutableArray<Seat> PassedCall,
    int Redeals,
    CallOption? HighestCall,
    Seat HighestCaller,
    ImmutableArray<Seat> CounterQueue,
    int CounterIndex,
    int CounterCount,
    bool CounterBeforeSwap,
    bool IsFirstRound,
    bool IsGrabRound,
    bool NextRoundIsGrab,
    bool PerTeamLevel,
    ImmutableArray<TrickPlay> TrickPlays,
    ImmutableArray<TrickRecord> History,
    Seat Leader,
    int DefenderTrickPoints,
    int DeclarerTrickPoints,
    ComboType LastTrickType,
    Team LastTrickWinnerTeam,
    int TrickNumber,
    GamePhase Phase,
    PendingDecision? Pending,
    Team RoomWinner);
