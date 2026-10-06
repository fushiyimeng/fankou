using FanKou.Ai;
using FanKou.Core.Game;
using FanKou.Core.Scoring;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 结算里的"庄家"必须指**刚打完那一局**的庄家。
/// 闲家赢了的话庄闲会对调，如果拿结算之后的引擎状态去显示，就会把庄闲说反——
/// 这正是之前结算框出的问题，这里钉住它。
/// </summary>
public class SettlementTests
{
    private static (IReadOnlyList<GameEvent> Events, GameEngine Engine) PlayOneRound(int seed)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            _ => (IPlayerBrain)new SimpleBrain(rng));

        var driver = new GameDriver(engine, brains);
        return (driver.RunRound(), engine);
    }

    private static Team DeclarerOf(IReadOnlyList<GameEvent> events) =>
        events.OfType<TrumpEstablished>().Last().Caller.TeamOf();

    [Fact]
    public void 闲家赢了下一局庄家就是闲家()
    {
        var covered = 0;

        for (var seed = 1; seed <= 400 && covered < 30; seed++)
        {
            var (events, _) = PlayOneRound(seed);
            var settled = events.OfType<RoundSettled>().Single();

            if (settled.Outcome is not (RoundOutcome.DefenderUp1 or RoundOutcome.DefenderUp2))
            {
                continue;
            }

            var declarer = DeclarerOf(events);
            var defenders = declarer.Other();
            var levels = events.OfType<LevelsChanged>().Single();

            Assert.Equal(defenders, levels.NextDeclarerTeam);
            Assert.True(
                levels.After[(int)defenders] >= levels.Before[(int)defenders],
                "闲家赢了，闲家的等级不该往下走");
            Assert.True(
                levels.After[(int)declarer] <= levels.Before[(int)declarer],
                "闲家赢了，庄家的等级不该往上走");

            covered++;
        }

        Assert.True(covered >= 30, $"400 个种子里只碰到 {covered} 局闲家赢，样本太少");
    }

    [Fact]
    public void 庄家赢了下一局庄家还是自己()
    {
        var covered = 0;

        for (var seed = 1; seed <= 400 && covered < 30; seed++)
        {
            var (events, _) = PlayOneRound(seed);
            var settled = events.OfType<RoundSettled>().Single();

            if (settled.Outcome is not (RoundOutcome.DeclarerUp1 or RoundOutcome.DeclarerUp2))
            {
                continue;
            }

            var declarer = DeclarerOf(events);
            var levels = events.OfType<LevelsChanged>().Single();

            Assert.Equal(declarer, levels.NextDeclarerTeam);
            Assert.True(levels.After[(int)declarer] >= levels.Before[(int)declarer]);

            covered++;
        }

        Assert.True(covered >= 30, $"400 个种子里只碰到 {covered} 局庄家赢，样本太少");
    }

    [Fact]
    public void 扮权时庄闲对调但谁都不升级()
    {
        var covered = 0;

        for (var seed = 1; seed <= 1500 && covered < 5; seed++)
        {
            var (events, _) = PlayOneRound(seed);
            var settled = events.OfType<RoundSettled>().Single();

            if (settled.Outcome != RoundOutcome.DefenderBanQuan)
            {
                continue;
            }

            var declarer = DeclarerOf(events);
            var levels = events.OfType<LevelsChanged>().Single();

            Assert.Equal(declarer.Other(), levels.NextDeclarerTeam);
            Assert.True(levels.After[(int)declarer] <= levels.Before[(int)declarer]);
            Assert.True(levels.After[(int)declarer.Other()] <= levels.Before[(int)declarer.Other()],
                "扮权时闲家不升级");

            covered++;
        }

        Assert.True(covered >= 5, $"1500 个种子里只碰到 {covered} 局扮权，样本太少");
    }

    [Fact]
    public void 过2之后输了也不掉级()
    {
        // 连打到有人过 2，再看它输一局会不会掉回去。
        for (var seed = 1; seed <= 200; seed++)
        {
            var rng = new SeededRandomSource(seed);
            var engine = new GameEngine(rng);
            var brains = Seats.All.ToDictionary(
                seat => seat,
                _ => (IPlayerBrain)new SimpleBrain(rng));
            var driver = new GameDriver(engine, brains);

            for (var round = 0; round < 25 && engine.Phase != GamePhase.RoomOver; round++)
            {
                var before = engine.Levels;
                var events = driver.RunRound();
                var settled = events.OfType<RoundSettled>().Single();
                var declarer = DeclarerOf(events);
                var levels = events.OfType<LevelsChanged>().Single();

                var losing = settled.Outcome switch
                {
                    RoundOutcome.DeclarerUp1 or RoundOutcome.DeclarerUp2 => declarer.Other(),
                    RoundOutcome.DefenderUp1 or RoundOutcome.DefenderUp2 => declarer,
                    _ => (Team?)null,
                };

                if (losing is not { } loser)
                {
                    continue;
                }

                var beforeIndex = levels.Before[(int)loser];
                var afterIndex = levels.After[(int)loser];

                if (FanKou.Core.Levels.LevelTrack.IsProtected(beforeIndex))
                {
                    Assert.Equal(beforeIndex, afterIndex);   // 过 2 了，不掉
                }
                else if (afterIndex != beforeIndex)
                {
                    Assert.Equal(0, afterIndex);             // 没过 2，掉回 5
                }
            }
        }
    }
}
