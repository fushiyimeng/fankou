using System.Collections.Immutable;
using FanKou.Ai;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 小无主和大无主。文档第 54 行："小无主，则是两张小王。大无主就是两张大王。"
/// 次序见文档第 14 行：叫主 &lt; 定主 &lt; 小无主 &lt; 大无主。
/// </summary>
public class NoTrumpCallTests
{
    private static ImmutableSortedSet<Rank> Level(Rank rank) => [rank];

    private static IReadOnlyList<CallOption> Calls(string hand, Rank levelRank = Rank.Five) =>
        CallFinder.FindAll(TestCards.Parse(hand), Level(levelRank), RuleOptions.Default);

    [Fact]
    public void 两张大王就能叫大无主()
    {
        var calls = Calls("大王 大王");

        var big = calls.SingleOrDefault(c => c.Kind == CallKind.BigNoTrump);

        Assert.NotNull(big);
        Assert.Null(big!.TrumpSuit);
        Assert.Equal(2, big.ShowCards.Length);
        Assert.All(big.ShowCards, c => Assert.True(c.IsBigJoker));
        Assert.Equal("大无主（两张大王）", big.Describe());
    }

    [Fact]
    public void 两张小王就能叫小无主()
    {
        var calls = Calls("小王 小王");

        var small = calls.SingleOrDefault(c => c.Kind == CallKind.SmallNoTrump);

        Assert.NotNull(small);
        Assert.Null(small!.TrumpSuit);
        Assert.All(small.ShowCards, c => Assert.True(c.IsSmallJoker));
        Assert.Equal("小无主（两张小王）", small.Describe());
    }

    [Fact]
    public void 只有一张王不能叫无主()
    {
        Assert.DoesNotContain(Calls("大王 ♠5"), c => c.Kind == CallKind.BigNoTrump);
        Assert.DoesNotContain(Calls("小王 ♠5"), c => c.Kind == CallKind.SmallNoTrump);
    }

    [Fact]
    public void 大小王各一张拼不出无主()
    {
        // 小无主要两张小王、大无主要两张大王，一张一王不行。
        var calls = Calls("小王 大王");
        Assert.DoesNotContain(calls, c => c.Kind is CallKind.SmallNoTrump or CallKind.BigNoTrump);
    }

    [Fact]
    public void 王和级牌都有的话几种叫法一起给出来()
    {
        var calls = Calls("小王 大王 大王 ♠5 ♠5");

        Assert.Contains(calls, c => c.Kind == CallKind.SuitedSuit);
        Assert.Contains(calls, c => c.Kind == CallKind.SuitedBoth);
        Assert.Contains(calls, c => c.Kind == CallKind.BigNoTrump);
    }

    [Fact]
    public void 规格高低就是叫主定主小无主大无主()
    {
        var calls = Calls("小王 小王 大王 大王 ♠5 ♠5")
            .Select(c => c.Priority)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(
            [
                (int)CallKind.SuitedSuit,
                (int)CallKind.SuitedBoth,
                (int)CallKind.SmallNoTrump,
                (int)CallKind.BigNoTrump,
            ],
            calls);
    }

    [Fact]
    public void 关掉开关就只剩有主的叫法()
    {
        var options = RuleOptions.Default with { AllowNoTrumpCalls = false };
        var calls = CallFinder.FindAll(TestCards.Parse("小王 小王 大王 大王 ♠5"), Level(Rank.Five), options);

        Assert.NotEmpty(calls);
        Assert.DoesNotContain(calls, c => c.Kind is CallKind.SmallNoTrump or CallKind.BigNoTrump);
    }

    [Fact]
    public void 随机自打里真的会出现无主局()
    {
        // 开关开了不等于真的用得上。这里跑一批随机局，确认无主确实会被叫出来。
        var noTrumpRounds = 0;

        for (var seed = 1; seed <= 200 && noTrumpRounds < 5; seed++)
        {
            var rng = new SeededRandomSource(seed);
            var engine = new GameEngine(rng);
            var brains = Seats.All.ToDictionary(
                seat => seat,
                _ => (IPlayerBrain)new RandomBrain(rng));
            var driver = new GameDriver(engine, brains);

            var events = driver.RunRound();

            if (events.OfType<TrumpEstablished>().Any(e => e.Option.TrumpSuit is null))
            {
                noTrumpRounds++;
            }
        }

        Assert.True(noTrumpRounds >= 5, $"200 局里只出现 {noTrumpRounds} 次无主，说明无主等于没生效");
    }

    [Fact]
    public void 大无主里级牌不再是主()
    {
        // 文档第 65 行："只有大小王是主，其它所有花色和当前等级牌都是副牌非主，
        // 5 已经不是主了，没有 6 大。"
        var trump = TrumpContext.BigNoTrump(Rank.Five);

        Assert.True(IdentityResolver.Resolve(TestCards.One("大王"), trump).IsTrump);
        Assert.False(IdentityResolver.Resolve(TestCards.One("♠5"), trump).IsTrump);

        // 5 不再是主，所以 ♠4455 是一副正常的连对（全非主，同一门）
        Assert.True(
            ComboRecognizer.TryRecognize(TestCards.Parse("♠4 ♠4 ♠5 ♠5"), trump, out var combo),
            "大无主下 ♠4455 应该是普通连对");
        Assert.False(combo!.IsTrump);
    }

    [Fact]
    public void 小无主里所有级牌一样大不分主副()
    {
        // 文档第 64 行 + 第 89 行："所有花色的当前级牌，一样大不分主副。"
        var trump = TrumpContext.SmallNoTrump(Rank.Five);

        var spade = IdentityResolver.Resolve(TestCards.One("♠5"), trump);
        var heart = IdentityResolver.Resolve(TestCards.One("♥5"), trump);

        Assert.Equal(spade.Order, heart.Order);
        Assert.True(spade.IsTrump);
        Assert.True(heart.IsTrump);
        Assert.False(IdentityResolver.Resolve(TestCards.One("♠A"), trump).IsTrump);
    }
}
