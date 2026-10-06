using FanKou.Ai;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

public class SimpleBrainTests
{
    private static (int EastWest, int NorthSouth, int Rounds) PlayMatch(int seed, bool eastWestIsHeuristic)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);

        var brains = Seats.All.ToDictionary(
            seat => seat,
            seat =>
            {
                var heuristic = (seat.TeamOf() == Team.EastWest) == eastWestIsHeuristic;
                return (IPlayerBrain)(heuristic ? new SimpleBrain(rng) : new RandomBrain(rng));
            });

        var driver = new GameDriver(engine, brains);
        var rounds = 0;

        while (engine.Phase != GamePhase.RoomOver && rounds < 60)
        {
            driver.RunRound();
            rounds++;
        }

        return (engine.LevelOf(Team.EastWest), engine.LevelOf(Team.NorthSouth), rounds);
    }

    [Fact]
    public void 简单档AI自己也跑得完()
    {
        for (var seed = 1; seed <= 600; seed++)
        {
            var rng = new SeededRandomSource(seed);
            var engine = new GameEngine(rng);
            var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new SimpleBrain(rng));
            var driver = new GameDriver(engine, brains);

            driver.RunRound();

            Assert.Equal(GamePhase.RoundOver, engine.Phase);
            Assert.All(Seats.All, seat => Assert.Empty(engine.HandOf(seat)));
        }
    }

    [Fact]
    public void 简单档AI比随机AI打得远()
    {
        // 同一批种子下，东西方向换成"中等 AI"，南北保持随机，
        // 跑完整盘比等级——这才叫"会配合"，光看单局分数是看不出来的。
        const int matches = 80;

        var heuristicEastWest = 0;
        var randomNorthSouth = 0;

        for (var seed = 1; seed <= matches; seed++)
        {
            var (eastWest, northSouth, _) = PlayMatch(seed, eastWestIsHeuristic: true);
            heuristicEastWest += eastWest;
            randomNorthSouth += northSouth;
        }

        Assert.True(
            heuristicEastWest > randomNorthSouth,
            $"带配合的 AI 应该打得更远，实际：{heuristicEastWest} vs {randomNorthSouth}");
    }
}
