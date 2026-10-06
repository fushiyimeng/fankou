using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

public class SeatOrderTests
{
    // 屏幕上的方位：东在右、南在下、西在左、北在上。
    // 所以枚举顺序 东→南→西→北 在屏幕上是顺时针。

    [Fact]
    public void 发牌是顺时针()
    {
        Assert.Equal(Seat.South, Seat.East.NextInDeal());
        Assert.Equal(Seat.West, Seat.South.NextInDeal());
        Assert.Equal(Seat.North, Seat.West.NextInDeal());
        Assert.Equal(Seat.East, Seat.North.NextInDeal());
    }

    [Fact]
    public void 出牌是逆时针()
    {
        Assert.Equal(Seat.North, Seat.East.NextInPlay());
        Assert.Equal(Seat.West, Seat.North.NextInPlay());
        Assert.Equal(Seat.South, Seat.West.NextInPlay());
        Assert.Equal(Seat.East, Seat.South.NextInPlay());
    }

    [Fact]
    public void 转四圈回到自己()
    {
        foreach (var seat in Seats.All)
        {
            Assert.Equal(seat, seat.NextInPlay().NextInPlay().NextInPlay().NextInPlay());
            Assert.Equal(seat, seat.NextInDeal().NextInDeal().NextInDeal().NextInDeal());
            Assert.Equal(seat, seat.Partner().Partner());
        }
    }

    [Fact]
    public void 对家不是自己而且同队()
    {
        foreach (var seat in Seats.All)
        {
            Assert.NotEqual(seat, seat.Partner());
            Assert.Equal(seat.TeamOf(), seat.Partner().TeamOf());
        }
    }
}

public class DealingTests
{
    private static GameEngine NewEngine(int seed) => new(new SeededRandomSource(seed));

    /// <summary>
    /// 把发牌和后面的反主都推完，推到"轮到换底牌"。
    /// 反主一律**不反**，这样庄家就是最早叫主的那个，方便断言。
    /// </summary>
    private static void DriveToBottomSwap(GameEngine engine)
    {
        var guard = 0;
        while (guard++ < 3000)
        {
            if (engine.Phase == GamePhase.BottomSwap)
            {
                return;
            }

            if (engine.Pending is PendingDecision.NeedCall call)
            {
                engine.Apply(call.IsCounter
                    ? new PassCall(call.Actor)
                    : call.Options.Count > 0
                        ? new DeclareCall(call.Actor, call.Options[0])
                        : new PassCall(call.Actor));
                continue;
            }

            if (engine.Phase == GamePhase.Dealing)
            {
                engine.AdvanceDeal();
                continue;
            }

            return;
        }
    }

    [Fact]
    public void 首局是一张一张发的()
    {
        var engine = NewEngine(3);
        engine.StartRound();

        Assert.Equal(GamePhase.Dealing, engine.Phase);

        // 刚开局手上还没牌
        Assert.All(Seats.All, seat => Assert.Empty(engine.HandOf(seat)));

        // 推一轮：每人一张
        engine.AdvanceDeal();
        Assert.All(Seats.All, seat => Assert.Single(engine.HandOf(seat)));

        engine.AdvanceDeal();
        Assert.All(Seats.All, seat => Assert.Equal(2, engine.HandOf(seat).Count));
    }

    [Fact]
    public void 第二局不再逐张发牌()
    {
        // 打完整一局，看第二局是不是瞬间发完。
        var rng = new SeededRandomSource(11);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            _ => (FanKou.Ai.IPlayerBrain)new FanKou.Ai.SimpleBrain(rng));
        var driver = new FanKou.Ai.GameDriver(engine, brains);

        driver.RunRound();
        Assert.Equal(GamePhase.RoundOver, engine.Phase);

        engine.StartRound();
        Assert.Equal(GamePhase.Calling, engine.Phase);
        Assert.All(Seats.All, seat => Assert.Equal(25, engine.HandOf(seat).Count));
    }

    [Fact]
    public void 首局发牌途中就能抢主()
    {
        for (var seed = 1; seed <= 400; seed++)
        {
            var engine = NewEngine(seed);
            engine.StartRound();

            var guard = 0;
            while (engine.Phase == GamePhase.Dealing && guard++ < 60)
            {
                if (engine.Pending is not PendingDecision.NeedCall needCall)
                {
                    engine.AdvanceDeal();
                    continue;
                }

                // 抢主的关键：叫的时候牌**还没发完**，不然就不叫"抢先"了。
                Assert.True(
                    engine.HandOf(needCall.Actor).Count < 25,
                    "抢主应该发生在发牌途中");
                Assert.NotEmpty(needCall.Options);

                // 抢主不给让牌的机会——有牌必须叫。
                Assert.False(needCall.CanPass);

                var caller = needCall.Actor;
                engine.Apply(new DeclareCall(caller, needCall.Options[0]));

                // 叫完了牌还得接着发完
                Assert.Equal(GamePhase.Dealing, engine.Phase);

                DriveToBottomSwap(engine);

                Assert.All(Seats.All, seat => Assert.Equal(25, engine.HandOf(seat).Count));
                Assert.Equal(GamePhase.BottomSwap, engine.Phase);
                Assert.Equal(caller, engine.DeclarerSeat);
                Assert.Equal(caller.TeamOf(), engine.DeclarerTeam);

                // 明牌就是手里那几张，而且被锁住不能扣。
                Assert.Equal(needCall.Options[0].ShowCards.Length, engine.Locked.Count);
                Assert.All(
                    needCall.Options[0].ShowCards,
                    card => Assert.Contains(engine.HandOf(caller), c => c.Id == card.Id));

                return;
            }
        }

        Assert.Fail("跑了 400 个种子都没人在发牌途中抢到庄，规则肯定哪里不对");
    }

    [Fact]
    public void 抢主用的明牌后面不能扣进底牌()
    {
        for (var seed = 1; seed <= 400; seed++)
        {
            var engine = NewEngine(seed);
            engine.StartRound();

            var guard = 0;
            while (engine.Phase == GamePhase.Dealing && guard++ < 60)
            {
                if (engine.Pending is not PendingDecision.NeedCall needCall)
                {
                    engine.AdvanceDeal();
                    continue;
                }

                engine.Apply(new DeclareCall(needCall.Actor, needCall.Options[0]));
                DriveToBottomSwap(engine);

                Assert.Equal(GamePhase.BottomSwap, engine.Phase);
                Assert.NotEmpty(engine.Locked);

                // 明牌不能被扣走
                var discards = engine.HandOf(needCall.Actor)
                    .Where(c => engine.Locked.All(l => l.Id != c.Id))
                    .Take(8)
                    .ToArray();

                Assert.Throws<InvalidOperationException>(() =>
                    engine.Apply(new CommitBottom(
                        needCall.Actor,
                        [.. engine.Locked.Take(1), .. discards.Take(7)])));

                return;
            }
        }

        Assert.Fail("没找到抢主的种子");
    }
}
