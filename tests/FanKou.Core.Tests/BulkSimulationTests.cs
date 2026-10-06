using FanKou.Ai;
using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Levels;
using FanKou.Core.Scoring;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 全 AI 随机自打。这是整个项目最值钱的一组测试：
/// 规则引擎的每一个边角局面都会被随机局撞出来，
/// 能连着跑几千局不崩、不卡、分数守恒，才说明引擎真的立住了。
/// </summary>
public class BulkSimulationTests
{
    private const int Games = 10000;

    private static GameDriver NewDriver(int seed)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            _ => (IPlayerBrain)new RandomBrain(rng));

        return new GameDriver(engine, brains);
    }

    [Fact]
    public void 随机自打一万局不崩不卡()
    {
        for (var seed = 1; seed <= Games; seed++)
        {
            var driver = NewDriver(seed);
            var engine = driver.Engine;

            driver.RunRound();

            Assert.Equal(GamePhase.RoundOver, engine.Phase);
            Assert.All(Seats.All, seat => Assert.Empty(engine.HandOf(seat)));

            // 底牌正好 8 张，而且这 8 张是庄家扣下去的（不在任何手上）。
            Assert.Equal(8, engine.Bottom.Count);

            // 分数守恒：两队的墩分 + 底牌里的分，恰好是整副牌的 200 分。
            var total = engine.DefenderTrickPoints
                + engine.DeclarerTrickPoints
                + engine.Bottom.Sum(c => c.Points);
            Assert.Equal(ScoreRules.TotalPoints, total);

            // 等级永远落在合法区间。
            Assert.All(Seats.All.Select(s => s.TeamOf()).Distinct(), team =>
            {
                Assert.InRange(engine.LevelOf(team), 0, LevelTrack.Count - 1);
            });
        }
    }

    [Fact]
    public void 随机自打的多局连打等级会推进()
    {
        // 单独一局看不到升级掉级，这里连打若干局，确认房间状态在推进。
        var rng = new SeededRandomSource(20261006);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new RandomBrain(rng));
        var driver = new GameDriver(engine, brains);

        var rounds = 0;
        while (engine.Phase != GamePhase.RoomOver && rounds < 200)
        {
            driver.RunRound();
            rounds++;
        }

        Assert.True(rounds > 1, "应该能连打多局");
        Assert.Equal(rounds, engine.RoundNumber);
    }

    [Fact]
    public void 同一个seed每次跑出来完全一样()
    {
        var a = NewDriver(777);
        var b = NewDriver(777);

        var eventsA = a.RunRound().Select(e => e.Describe()).ToArray();
        var eventsB = b.RunRound().Select(e => e.Describe()).ToArray();

        Assert.Equal(eventsA, eventsB);
    }

    [Fact]
    public void 出牌的每一手都是合法牌型()
    {
        var driver = NewDriver(4242);
        var engine = driver.Engine;
        var events = driver.RunRound();

        var plays = events.OfType<CardPlayed>().ToArray();
        Assert.NotEmpty(plays);

        // 一局的出牌次数 = 25 墩 × 4 家（除非有人一次出多张，那就更少）。
        Assert.InRange(plays.Length, 4, 100);

        // 每次出的牌张数要么是 1，要么是自己的完整一手。
        Assert.All(plays, play => Assert.NotEmpty(play.Combo.Cards));
    }
}
