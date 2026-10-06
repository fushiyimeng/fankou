using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Levels;
using FanKou.Core.Scoring;
using FanKou.Core.Trump;

namespace FanKou.Core.Game;

/// <summary>
/// 引擎产生的事件。界面拿它播动画，命令行工具拿 <see cref="Describe"/> 打日志。
/// </summary>
public abstract record GameEvent
{
    /// <summary>给玩家看的中文描述。</summary>
    public abstract string Describe();
}

public sealed record RoundStarted(int RoundNumber, ImmutableArray<int> Levels, Team? DeclarerTeam) : GameEvent
{
    public override string Describe() => DeclarerTeam is { } team
        ? $"===== 第 {RoundNumber} 局开始，{team.Name()} 坐庄，本局打 {LevelTrack.RankAt(Levels[(int)team]).Text()} ====="
        : $"===== 第 {RoundNumber} 局开始（首局，还没有庄家），"
          + $"东西打 {LevelTrack.Describe(Levels[0])}、南北打 {LevelTrack.Describe(Levels[1])}，发牌途中谁先叫得出来谁坐庄 =====";
}

/// <summary>发牌进度。首局是一张一张发的，每发完一轮（每人一张）报一次。</summary>
public sealed record DealProgress(int Round, int Total) : GameEvent
{
    public override string Describe() => $"发牌 {Round} / {Total} 轮";
}

public sealed record Redeal(string Reason) : GameEvent
{
    public override string Describe() => $"{Reason}，本局重洗重发（庄闲身份不变）";
}

public sealed record CallMade(Seat Seat, CallOption Option) : GameEvent
{
    public override string Describe() => $"{Seat.Name()} 家 {Option.DescribeWithCards()}";
}

public sealed record CallPassed(Seat Seat) : GameEvent
{
    public override string Describe() => $"{Seat.Name()} 家不叫";
}

/// <summary>反主：用更高的规格把主牌抢过来（文档第 14、55 行）。</summary>
public sealed record CounterCalled(Seat Seat, CallOption Option) : GameEvent
{
    public override string Describe() => $"{Seat.Name()} 家反主！{Option.DescribeWithCards()}";
}

/// <summary>反主结束了：有人反到了底，或者没人再反。</summary>
public sealed record CounterCallingSettled(Seat FinalCaller, CallOption FinalOption, int CounterCount) : GameEvent
{
    public override string Describe() => CounterCount == 0
        ? "没人反主，主牌就这样定了"
        : $"反主结束，最后是 {FinalCaller.Name()} 家的 {FinalOption.Describe()}，反了 {CounterCount} 次";
}

public sealed record TrumpEstablished(CallOption Option, Seat Caller, Rank LevelRank) : GameEvent
{
    public override string Describe() =>
        $"{Caller.Name()} 家成为庄家，{Option.Describe()}，本局级牌 {LevelRank.Text()}";
}

/// <summary>
/// 抢主局定庄了：抢到的那队保住等级，没抢到的那队掉级（文档第 86 行）。
/// </summary>
public sealed record GrabRoundSettled(Team Grabber, Team Loser, bool Demoted) : GameEvent
{
    public override string Describe() => Demoted
        ? $"{Grabber.Name()} 抢到了庄，{Loser.Name()} 没抢到，掉级到 5"
        : $"{Grabber.Name()} 抢到了庄，{Loser.Name()} 没抢到（已过 2 不掉级）";
}

/// <summary>庄家无牌叫不起，判负掉级，闲家接庄（文档第 43、94 行）。</summary>
public sealed record DeclarerHandover(Team From, Team To, bool Demoted) : GameEvent
{
    public override string Describe() => Demoted
        ? $"{From.Name()} 叫不起，判负掉级到 5，{To.Name()} 接庄"
        : $"{From.Name()} 叫不起，判负（已过 2 不掉级），{To.Name()} 接庄";
}

public sealed record BottomExchanged(Seat Seat, int Count) : GameEvent
{
    public override string Describe() => $"{Seat.Name()} 家换底牌，扣下 {Count} 张";
}

public sealed record CardPlayed(Seat Seat, CardCombination Combo) : GameEvent
{
    public override string Describe() => $"{Seat.Name()} 家出 {Combo.Describe()}";
}

public sealed record TrickWon(
    Seat Winner,
    int Points,
    bool CountedForDefenders,
    int TrickNumber,
    ImmutableArray<TrickPlay> Plays) : GameEvent
{
    public override string Describe() => Points == 0
        ? $"第 {TrickNumber} 轮 {Winner.Name()} 家最大，本轮没分"
        : CountedForDefenders
            ? $"第 {TrickNumber} 轮 {Winner.Name()} 家最大，闲家捡到 {Points} 分"
            : $"第 {TrickNumber} 轮 {Winner.Name()} 家最大，庄家扣掉 {Points} 分";
}

public sealed record BottomRevealed(BottomResult Result, ImmutableArray<Card> Cards) : GameEvent
{
    public override string Describe() =>
        $"翻开底牌：{CardText.Format(Cards)} —— {Result.Describe()}";
}

public sealed record RoundSettled(RoundOutcome Outcome, int DefenderPoints) : GameEvent
{
    public override string Describe() =>
        $"闲家本局共 {DefenderPoints} 分 → {RoundResultEvaluator.Describe(Outcome)}";
}

/// <param name="NextDeclarerTeam">
/// **下一局**谁坐庄。注意不是刚打完那一局的庄家——闲家赢了的话庄闲是要对调的。
/// </param>
public sealed record LevelsChanged(
    ImmutableArray<int> Before,
    ImmutableArray<int> After,
    Team NextDeclarerTeam) : GameEvent
{
    public override string Describe()
    {
        var before = $"东西 {LevelTrack.Describe(Before[0])} / 南北 {LevelTrack.Describe(Before[1])}";
        var after = $"东西 {LevelTrack.Describe(After[0])} / 南北 {LevelTrack.Describe(After[1])}";
        return $"{before} → {after}（下一局 {NextDeclarerTeam.Name()} 坐庄）";
    }
}

public sealed record RoomFinished(Team Winner) : GameEvent
{
    public override string Describe() =>
        $"★ {Winner.Name()} 打到 A 又赢了一局，整局结束，可以换座位或者换人重开 ★";
}
