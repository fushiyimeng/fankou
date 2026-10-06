using FanKou.Ai;
using FanKou.Core.Bidding;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Levels;
using FanKou.Core.Scoring;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 反主。文档第 14、55 行：规格 叫主 &lt; 定主 &lt; 小无主 &lt; 大无主；
/// **同规格不能反，队友不能反，只有对手能反**，一直可以反到双大王为止。
///
/// 文档第 87 行：反主只改主牌和"谁拿底牌换牌"，不改庄闲身份、也不改谁先出牌；
/// 只有首局无庄的时候，谁最后反成功谁是庄家。
///
/// 注意：反主不是每局都有——对手手上得真凑得出更高的规格才行。
/// 所以这里都是"扫种子找有反主的局"，而不是指定一个种子。
/// </summary>
public class CounterCallTests
{
    private static (IReadOnlyList<GameEvent> Events, GameEngine Engine) PlayRound(int seed, RuleOptions? options = null)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng, options);
        var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new SimpleBrain(rng));

        var driver = new GameDriver(engine, brains);
        return (driver.RunRound(), engine);
    }

    [Fact]
    public void 反主真的会发生()
    {
        var counterRounds = 0;

        for (var seed = 1; seed <= 300 && counterRounds < 10; seed++)
        {
            var (events, _) = PlayRound(seed);

            if (events.OfType<CounterCalled>().Any())
            {
                counterRounds++;
            }
        }

        Assert.True(counterRounds >= 10, $"300 局里只有 {counterRounds} 局出现反主，反主等于没生效");
    }

    [Fact]
    public void 每一次反的规格都比上一次高()
    {
        var checkedCounters = 0;

        for (var seed = 1; seed <= 300 && checkedCounters < 20; seed++)
        {
            var (events, _) = PlayRound(seed);

            var maxSoFar = 0;
            var sawCounter = false;

            foreach (var e in events)
            {
                switch (e)
                {
                    case CounterCalled counter:
                        // 反主必须**严格**抬规格（同规格不能反，文档第 55 行）
                        Assert.True(
                            counter.Option.Priority > maxSoFar,
                            $"反主规格只能往上抬：这次 {counter.Option.Priority}，之前最高 {maxSoFar}");
                        maxSoFar = counter.Option.Priority;
                        sawCounter = true;
                        checkedCounters++;
                        break;

                    case TrumpEstablished established:
                        // 定主只是把当前最高规格复述一遍，不该比之前低
                        Assert.True(
                            established.Option.Priority >= maxSoFar,
                            $"定主的规格不该往下掉：{established.Option.Priority} < {maxSoFar}");
                        maxSoFar = Math.Max(maxSoFar, established.Option.Priority);
                        break;
                }
            }

            if (!sawCounter)
            {
                continue;
            }
        }

        Assert.True(checkedCounters > 0, "没找到带反主的局");
    }

    [Fact]
    public void 反主只改主牌不改庄闲()
    {
        for (var seed = 1; seed <= 300; seed++)
        {
            var (events, engine) = PlayRound(seed);

            if (!events.OfType<CounterCalled>().Any())
            {
                continue;
            }

            var established = events.OfType<TrumpEstablished>().ToArray();
            var firstCaller = established[0].Caller;

            // 反完之后主牌确实变了：最后一次"定主"的人（如果反过）跟最初叫的人不是一队
            var lastCaller = established[^1].Caller;

            if (established.Length > 1)
            {
                Assert.NotEqual(firstCaller.TeamOf(), lastCaller.TeamOf());
            }

            // 不管怎么反，**先出牌的还是自己那一队的庄家**（文档第 87 行"庄家先出牌"）
            var firstPlay = events.OfType<CardPlayed>().First();
            Assert.Equal(engine.DeclarerTeam, firstPlay.Seat.TeamOf());

            return;
        }

        Assert.Fail("没找到带反主的局");
    }

    [Fact]
    public void 关掉反主开关还能正常打完()
    {
        var options = RuleOptions.Default with { AllowCounterCalls = false };

        for (var seed = 1; seed <= 100; seed++)
        {
            var (events, engine) = PlayRound(seed, options);

            Assert.Equal(GamePhase.RoundOver, engine.Phase);
            Assert.DoesNotContain(events, e => e is CounterCalled);
        }
    }

    [Fact]
    public void 抢主局定庄时没抢到的那队掉级()
    {
        var checkedGrab = 0;

        for (var seed = 1; seed <= 400 && checkedGrab < 3; seed++)
        {
            var rng = new SeededRandomSource(seed);
            var engine = new GameEngine(rng);
            var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new SimpleBrain(rng));
            var driver = new GameDriver(engine, brains);

            for (var round = 0; round < 20 && engine.Phase != GamePhase.RoomOver; round++)
            {
                var beforeLevels = engine.Levels;
                var events = driver.RunRound();

                if (events.OfType<RoundSettled>().Single().Outcome != RoundOutcome.GrabRound)
                {
                    continue;
                }

                // 下一局改成"发牌途中抢先叫主"，定庄的时候没抢到的掉级
                var nextEvents = driver.RunRound();
                var grabSettled = nextEvents.OfType<GrabRoundSettled>().FirstOrDefault();

                Assert.NotNull(grabSettled);

                Assert.Equal(
                    LevelTrack.Demote(beforeLevels[(int)grabSettled!.Loser]),
                    engine.Levels[(int)grabSettled.Loser]);

                Assert.True(
                    engine.Levels[(int)grabSettled.Grabber] >= beforeLevels[(int)grabSettled.Grabber],
                    "抢到的那队等级不该往下走");

                checkedGrab++;
                break;
            }
        }

        Assert.True(checkedGrab >= 3, $"400 个种子只碰到 {checkedGrab} 次抢主局");
    }

}
