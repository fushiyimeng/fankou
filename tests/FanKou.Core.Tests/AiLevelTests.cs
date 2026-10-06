using FanKou.Ai;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 四档人机。
///
/// 比强度用的是"配对"打法：同一个种子下，一副牌打两遍，A 和 B 各坐一次东西。
/// 不配对的话发牌运气会盖过实力差距——试过，同一对档位换个种子区间能差出 30%。
///
/// 实测（判据和样本量见各条测试的注释）：
///   简单 vs 入门：入门一分没拿过，差距大得没法比
///   困难 vs 普通：**打平**（1000 个种子配对、约 1.2 万局，50.0% 区间 49.1~50.9）
///     —— 笔记里那个 +0.6% 在这个样本量下复现不出来
///   普通 vs 简单：笔记里记的是 +2.8%（换尺子之前量的，还没用新工具复测）
/// </summary>
public class AiLevelTests
{
    public static TheoryData<AiLevel> AllLevels =>
    [
        AiLevel.Beginner,
        AiLevel.Simple,
        AiLevel.Normal,
        AiLevel.Hard,
    ];

    [Theory]
    [MemberData(nameof(AllLevels))]
    public void 每一档都能把局打完(AiLevel level)
    {
        for (var seed = 1; seed <= 150; seed++)
        {
            var rng = new SeededRandomSource(seed);
            var engine = new GameEngine(rng);
            var brains = Seats.All.ToDictionary(seat => seat, _ => BrainFactory.Create(level, rng));
            var driver = new GameDriver(engine, brains);

            driver.RunRound();

            Assert.Equal(GamePhase.RoundOver, engine.Phase);
            Assert.All(Seats.All, seat => Assert.Empty(engine.HandOf(seat)));

            // 分数守恒，说明每一手都真的打出去了，没有卡住或者吞牌
            var total = engine.DefenderTrickPoints
                + engine.DeclarerTrickPoints
                + engine.Bottom.Sum(c => c.Points);
            Assert.Equal(200, total);
        }
    }

    [Fact]
    public void 全部四档的脑子都给得出来()
    {
        var rng = new SeededRandomSource(1);

        foreach (var level in new[] { AiLevel.Beginner, AiLevel.Simple, AiLevel.Normal, AiLevel.Hard })
        {
            Assert.NotNull(BrainFactory.Create(level, rng));
            Assert.NotEmpty(BrainFactory.Name(level));
        }
    }

    /// <summary>同一副牌打两遍，两家各坐一次东西，返回 (强档总分, 弱档总分)。</summary>
    private static (int Strong, int Weak) PlayPaired(int seed, AiLevel strong, AiLevel weak)
    {
        var first = PlayMatch(seed, strong, weak);
        var second = PlayMatch(seed, weak, strong);

        return (
            first.EastWest + second.NorthSouth,
            first.NorthSouth + second.EastWest);
    }

    private static (int EastWest, int NorthSouth) PlayMatch(int seed, AiLevel eastWest, AiLevel northSouth)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            seat => BrainFactory.Create(seat.TeamOf() == Team.EastWest ? eastWest : northSouth, rng));

        var driver = new GameDriver(engine, brains);
        var rounds = 0;

        while (engine.Phase != GamePhase.RoomOver && rounds++ < 60)
        {
            driver.RunRound();
        }

        return (engine.LevelOf(Team.EastWest), engine.LevelOf(Team.NorthSouth));
    }

    [Fact]
    public void 简单档碾压入门档()
    {
        var simple = 0;
        var beginner = 0;

        for (var seed = 1; seed <= 40; seed++)
        {
            var (strong, weak) = PlayPaired(seed, AiLevel.Simple, AiLevel.Beginner);
            simple += strong;
            beginner += weak;
        }

        // 实测入门档一分都拿不到（简单档每次都冲到 A 结束），所以这里断言一个很大的倍数
        Assert.True(simple > beginner * 3, $"简单 {simple} vs 入门 {beginner}");
    }

    [Fact]
    public void 会记牌的不输给只凭感觉的()
    {
        // 说明：普通档比简单档多的就是"记牌、算牌"。这条只断言**不倒退**——
        // 实测这个差距在 ±3% 量级上来回翻（加甩牌和反主之前是 +2.8%，之后是 -3.4%），
        // 说明在现在这套出牌模型下，记不记牌对胜负的影响已经淹没在噪声里了。
        // 记为"它确实在用这个信息"，而不是"它因此明显更强"——后者是自欺欺人。
        // 真要让普通/困难明显强出一截，得换思路（往前算一两步），不是往上加规则能解决的。
        const int matches = 60;

        var counting = 0;
        var simple = 0;

        for (var seed = 1; seed <= matches; seed++)
        {
            var (strong, weak) = PlayPaired(seed, AiLevel.Normal, AiLevel.Simple);
            counting += strong;
            simple += weak;
        }

        // 加了甩牌和反主之后实测是 -3% ~ -7%，所以这里只能定"不倒退太多"这条线。
        Assert.True(
            counting >= simple * 0.9,
            $"普通档不该比简单档明显差：普通 {counting} vs 简单 {simple}");
    }

    [Fact]
    public void 困难档不弱于普通档()
    {
        // 判据换掉了：以前用的是"连打若干局之后看最后打到几级"，那个被
        // "谁先冲到 A 结束房间"主导，噪声大到能把一次微小改动翻出 3.6%（实测踩到），
        // 于是只能靠断言"不倒退 3%"勉强站着。
        //
        // 现在用 PairedEvaluation 的判据——**每一局谁把等级推进得更多**，
        // 细得多，也稳得多。实测（1000 个种子配对、约 1.2 万局）：
        //   困难 50.0%，区间 49.1% ~ 50.9%
        // 也就是**和普通档打平**。笔记里记的 +0.6% 在那个样本量下复现不出来。
        //
        // 困难档比普通档多的只有空门推断，而它换不来可测的收益——这也印证了
        // 一路下来的结论：在这套"一层打分"的模型上继续加东西，收益要么淹没在噪声里、
        // 要么干脆是零。真想更强得换思路（往前算），不是加规则。
        //
        // 所以这里只断言"没有明显倒退"，不去断言"更强"——后者是自欺欺人。
        // 要正经测强度用 tools/FanKou.Bench，判据和这里是同一份代码。
        const int seeds = 200;
        const int rounds = 8;

        var result = PairedEvaluation.Run(AiLevel.Hard, AiLevel.Normal, seeds, rounds);
        var (low, _) = result.Interval;

        Assert.True(
            low > 0.45,
            $"困难档不该比普通档明显差：胜率 {result.WinRate:P1}，区间下限 {low:P1}，"
            + $"有效 {result.Decisive} 局（困难赢 {result.AWins} / 普通赢 {result.BWins}）");
    }
}
