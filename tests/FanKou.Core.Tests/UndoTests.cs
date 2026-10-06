using System.Text;
using FanKou.Ai;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 「撤回」的地基：<see cref="GameEngine.CaptureState"/> / <see cref="GameEngine.RestoreState"/>
/// 能不能把引擎原样放回去。
///
/// 这里全部用公开 API 驱动引擎，不经过界面——界面的那层派生状态（日志、明牌、计时器）
/// 归 <c>GameController</c> 管，不在这一组里测。
///
/// 最要命的一类 bug 是快照**漏字段**：编译器不会报错，只会在撤回之后表现为某个数字
/// 悄悄不对。<see cref="还原之后重放同样的命令结果完全一致"/> 就是拿来逼它的——
/// 它跑过一整墩，任何影响后续走子的字段漏了都会让重放结果对不上。
/// </summary>
public class UndoTests
{
    private static (GameEngine Engine, GameDriver Driver) NewGame(int seed)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new RandomBrain(rng));

        return (engine, new GameDriver(engine, brains));
    }

    /// <summary>推进一步：有决策就做一个，发牌阶段就发一轮。返回 false 表示推不动了。</summary>
    private static bool Step(GameEngine engine, GameDriver driver)
    {
        if (engine.Pending is { } pending)
        {
            engine.Apply(driver.Decide(pending));
            return true;
        }

        if (engine.Phase == GamePhase.Dealing)
        {
            engine.AdvanceDeal();
            return true;
        }

        return false;
    }

    /// <summary>开一局，一直推到出牌阶段。</summary>
    private static void AdvanceToPlaying(GameEngine engine, GameDriver driver)
    {
        engine.StartRound();

        var guard = 0;

        while (engine.Phase != GamePhase.Playing && guard++ < 100_000)
        {
            Assert.True(Step(engine, driver), "引擎推进不动了");
        }

        Assert.Equal(GamePhase.Playing, engine.Phase);
    }

    /// <summary>推到这个局面为止，找不到就断言失败。</summary>
    private static void AdvanceUntil(GameEngine engine, GameDriver driver, Func<GameEngine, bool> stop, string what)
    {
        var guard = 0;

        while (!stop(engine) && guard++ < 100_000)
        {
            Assert.True(Step(engine, driver), "引擎推进不动了");
        }

        Assert.True(stop(engine), $"应该能走到「{what}」");
    }

    /// <summary>
    /// 把引擎的**全部公开状态**压成一个字符串。
    /// 牌用 <see cref="Card.Id"/> 而不是 <c>ToString()</c>——两副牌里同名牌长得一样，
    /// 渲染出来的字面量分不出 Copy，那样漏牌也测不出来。
    /// </summary>
    private static string Fingerprint(GameEngine engine)
    {
        var sb = new StringBuilder();

        sb.Append("phase=").Append(engine.Phase)
          .Append(";round=").Append(engine.RoundNumber)
          .Append(";declarer=").Append(engine.DeclarerTeam).Append('/').Append(engine.HasDeclarer).Append('/').Append(engine.DeclarerSeat)
          .Append(";trump=").Append(engine.Trump?.Describe() ?? "-")
          .Append(";levelRank=").Append(engine.LevelRank)
          .Append(";levels=").Append(string.Join(',', engine.Levels))
          .Append(";trickNo=").Append(engine.TrickNumber)
          .Append(";points=").Append(engine.DefenderTrickPoints).Append('/').Append(engine.DeclarerTrickPoints)
          .Append(";roomWinner=").Append(engine.RoomWinner)
          .Append(";bottom=").Append(Keys(engine.Bottom))
          .Append(";locked=").Append(Keys(engine.Locked));

        foreach (var seat in Seats.All)
        {
            sb.Append(";hand.").Append(seat).Append('=').Append(Keys(engine.HandOf(seat)));
        }

        sb.Append(";trick=").Append(string.Join(
            '|',
            engine.CurrentTrick.Select(p => $"{p.Seat}:{Combo(p.Combo)}")));

        sb.Append(";history=").Append(string.Join(
            '|',
            engine.History.Select(h =>
                $"{h.TrickNumber}/{h.Winner}/{h.Points}/{h.DefenderWon}/{string.Join('.', h.Plays.Select(p => Combo(p.Combo)))}")));

        sb.Append(";pending=").Append(Pending(engine.Pending));

        return sb.ToString();
    }

    private static string Pending(PendingDecision? pending) => pending switch
    {
        null => "-",
        PendingDecision.NeedCall call =>
            $"call:{call.Actor}/{call.IsCounter}/{call.CanPass}/{call.Options.Count}/{Keys(call.Hand)}",
        PendingDecision.NeedBottomSwap swap =>
            $"swap:{swap.Actor}/{Keys(swap.Hand)}/{Keys(swap.Bottom)}",
        PendingDecision.NeedPlay play =>
            $"play:{play.Actor}/{play.Lead?.Describe() ?? "-"}/{play.Candidates.Count}/{Keys(play.Hand)}",
        _ => "?",
    };

    private static string Keys(IEnumerable<Card> cards) => string.Join('.', cards.Select(c => c.Id));

    private static string Combo(CardCombination combo) => $"{combo.Type}[{Keys(combo.Cards)}]";

    // ---------------------------------------------------------------- 测试

    [Fact]
    public void 存盘还原之后状态一模一样()
    {
        var (engine, driver) = NewGame(20261006);
        AdvanceToPlaying(engine, driver);

        // 先走几手，让局面里有东西可存：已打完的墩、分数、当前墩。
        for (var i = 0; i < 9; i++)
        {
            Assert.True(Step(engine, driver));
        }

        var before = Fingerprint(engine);
        var saved = engine.CaptureState();

        for (var i = 0; i < 12; i++)
        {
            Assert.True(Step(engine, driver));
        }

        Assert.NotEqual(before, Fingerprint(engine));

        engine.RestoreState(saved);

        Assert.Equal(before, Fingerprint(engine));
    }

    [Fact]
    public void 还原之后重放同样的命令结果完全一致()
    {
        // 这条是整套机制的核心保证：还原之后把同样的命令再喂一遍，
        // 终点必须和第一次跑出来的状态逐字节一致。
        // 快照漏了任何一个影响后续走子的字段，这里都会对不上。
        var (engine, driver) = NewGame(31337);
        AdvanceToPlaying(engine, driver);

        // 撤回点停在"上一墩刚打完、新一墩还没人出牌"的那一刻——这是最贴近真实用法的位置
        // （用户就是在一墩开头轮到自己时出牌）。
        //
        // 顺带记一条实测结论：_leader **并不需要**还原。它每打完一墩都会被 ResolveTrick
        // 重新赋值，下一次读它之前必然已经刷新过，所以把它从 RestoreState 里删掉
        // 不会有任何后果（试过，全绿）。它还是留在快照里，是为了将来万一在别的阶段
        // 用撤回时不出意外。
        //
        // 反过来，_defenderTrickPoints 这类字段删掉就会被这条测试逮住（也试过）——
        // 这条测试确实有牙齿，别把它当形式。
        AdvanceUntil(
            engine,
            driver,
            e => e.Pending is PendingDecision.NeedPlay && e.CurrentTrick.Count == 0 && e.TrickNumber > 0,
            "新一墩的开头");

        var saved = engine.CaptureState();

        // 一路打到本局结束。走满整局才能把结算用到的字段
        // （_lastTrickType、_lastTrickWinnerTeam、底牌倍率那些）也覆盖进去。
        var commands = new List<GameCommand>();
        var guard = 0;

        while (guard++ < 1000 && engine.Phase == GamePhase.Playing && engine.Pending is { } pending)
        {
            var command = driver.Decide(pending);
            commands.Add(command);
            engine.Apply(command);
        }

        Assert.True(commands.Count > 20, "应该能一路打到本局结束");
        var expected = Fingerprint(engine);

        engine.RestoreState(saved);
        Assert.NotEqual(expected, Fingerprint(engine));

        foreach (var command in commands)
        {
            engine.Apply(command);
        }

        Assert.Equal(expected, Fingerprint(engine));
    }

    [Fact]
    public void 还原出来的还是原来那个出牌机会()
    {
        // 真实用法就长这样：撤回点是在"轮到用户出牌"那一刻抓的，
        // 还原之后必须还给他同一个出牌机会，候选也得还是那些。
        var (engine, driver) = NewGame(2);
        AdvanceToPlaying(engine, driver);

        AdvanceUntil(
            engine,
            driver,
            e => e.Pending is PendingDecision.NeedPlay { Actor: Seat.South },
            "轮到南家出牌");

        var saved = engine.CaptureState();
        var candidates = ((PendingDecision.NeedPlay)engine.Pending!).Candidates;
        Assert.NotEmpty(candidates);

        var played = candidates[0].Cards;
        engine.Apply(new PlayCards(Seat.South, played));

        for (var i = 0; i < 5; i++)
        {
            Step(engine, driver);
        }

        engine.RestoreState(saved);

        var restored = Assert.IsType<PendingDecision.NeedPlay>(engine.Pending);
        Assert.Equal(Seat.South, restored.Actor);
        Assert.Equal(candidates.Count, restored.Candidates.Count);

        // 刚才打出去的那几张牌得原样回到手上。
        Assert.All(played, card => Assert.Contains(card, engine.HandOf(Seat.South)));
    }

    [Fact]
    public void 撤回把这一墩的结算也退回去()
    {
        var (engine, driver) = NewGame(99);
        AdvanceToPlaying(engine, driver);

        // 等到"前三家都出完了，就差最后一张"——这时南家（最后一家）一出，这一墩立刻结算。
        AdvanceUntil(
            engine,
            driver,
            e => e.Pending is PendingDecision.NeedPlay && e.CurrentTrick.Count == 3,
            "一墩就差最后一张");

        var saved = engine.CaptureState();
        var trickNo = engine.TrickNumber;
        var points = engine.DefenderTrickPoints + engine.DeclarerTrickPoints;
        var history = engine.History.Count;

        var last = (PendingDecision.NeedPlay)engine.Pending!;
        engine.Apply(new PlayCards(last.Actor, last.Candidates[0].Cards));

        Assert.True(engine.TrickNumber > trickNo, "第四张一出，这一墩就该结算");
        Assert.Equal(history + 1, engine.History.Count);

        engine.RestoreState(saved);

        Assert.Equal(trickNo, engine.TrickNumber);
        Assert.Equal(points, engine.DefenderTrickPoints + engine.DeclarerTrickPoints);
        Assert.Equal(history, engine.History.Count);
        Assert.Equal(3, engine.CurrentTrick.Count);
    }
}
