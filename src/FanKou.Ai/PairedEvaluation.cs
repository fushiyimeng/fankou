using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 两档 AI 的强度尺子。**工具和测试共用这一份**——分成两份迟早会跑偏，
/// 到时候"工具说涨了、测试说跌了"，不知道信谁。
///
/// 两条设计要点：
///
/// 1. **配对**。同一个种子下把同一副牌打两遍，A 和 B 各坐一次东西，发牌运气就抵消了。
///    不配对的话，同一对档位换个种子区间能差出 30%。
/// 2. **判据是"这一局谁把等级推进得更多"**，也就是游戏真正的目标。
///    不要用"最后打到几级"——那个被"谁先冲到 A 结束房间"主导，
///    噪声大到能把一次微小改动翻出 3.6%（实测过，见 AiLevelTests 的注释）。
/// </summary>
public static class PairedEvaluation
{
    public sealed record Result(int AWins, int BWins, int Ties)
    {
        public int Decisive => AWins + BWins;

        /// <summary>A 的胜率，只算分得出胜负的局。没有有效局时给 0.5。</summary>
        public double WinRate => Decisive == 0 ? 0.5 : AWins / (double)Decisive;

        /// <summary>95% 置信区间（Wilson）。样本少的时候比正态近似靠谱得多。</summary>
        public (double Low, double High) Interval =>
            Wilson(AWins, Decisive);

        public Result Plus(Result other) =>
            new(AWins + other.AWins, BWins + other.BWins, Ties + other.Ties);
    }

    /// <summary>
    /// 跑一批配对对局。<paramref name="seeds"/> 个种子，每个种子换边打两遍，
    /// 每遍打 <paramref name="rounds"/> 局。
    /// </summary>
    public static Result Run(AiLevel a, AiLevel b, int seeds, int rounds)
    {
        var total = new Result(0, 0, 0);
        var gate = new object();

        Parallel.For(1, seeds + 1, seed =>
        {
            var one = Play(seed, a, b, Team.EastWest, rounds)
                .Plus(Play(seed, a, b, Team.NorthSouth, rounds));

            lock (gate)
            {
                total = total.Plus(one);
            }
        });

        return total;
    }

    /// <summary>一个种子下打一遍：A 固定坐 <paramref name="aTeam"/>，B 坐另一家。</summary>
    public static Result Play(int seed, AiLevel a, AiLevel b, Team aTeam, int rounds)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            seat => BrainFactory.Create(seat.TeamOf() == aTeam ? a : b, rng));

        var driver = new GameDriver(engine, brains);

        var aWins = 0;
        var bWins = 0;
        var ties = 0;

        for (var round = 0; round < rounds && engine.Phase != GamePhase.RoomOver; round++)
        {
            var events = driver.RunRound();

            // 这一局谁把等级推进得更多，谁就赢这一局。
            // 不去判断"谁是庄家"——等级变化本身已经把胜负写清楚了，
            // 抢主局和扮权那种两边都不动的情况也自然落进平局。
            var change = events.OfType<LevelsChanged>().LastOrDefault();
            if (change is null)
            {
                continue;
            }

            var ewDelta = change.After[0] - change.Before[0];
            var nsDelta = change.After[1] - change.Before[1];

            var aDelta = aTeam == Team.EastWest ? ewDelta : nsDelta;
            var bDelta = aTeam == Team.EastWest ? nsDelta : ewDelta;

            if (aDelta > bDelta)
            {
                aWins++;
            }
            else if (bDelta > aDelta)
            {
                bWins++;
            }
            else
            {
                ties++;
            }
        }

        return new Result(aWins, bWins, ties);
    }

    private static (double Low, double High) Wilson(int wins, int total)
    {
        if (total == 0)
        {
            return (0, 1);
        }

        const double Z = 1.96;
        var p = wins / (double)total;
        var denominator = 1 + (Z * Z / total);
        var center = (p + (Z * Z / (2.0 * total))) / denominator;
        var margin = Z * Math.Sqrt((p * (1 - p) / total) + (Z * Z / (4.0 * total * total))) / denominator;

        return (Math.Max(0, center - margin), Math.Min(1, center + margin));
    }
}
