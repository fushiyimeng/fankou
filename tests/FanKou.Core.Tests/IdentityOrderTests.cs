using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

public class IdentityOrderTests
{
    /// <summary>黑桃 5 主——规则文档里反复用的那个例子。</summary>
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static CardIdentity Id(string token, TrumpContext context) =>
        IdentityResolver.Resolve(TestCards.One(token), context);

    private static void AssertDescending(TrumpContext context, params string[] tokens)
    {
        var ids = tokens.Select(t => Id(t, context)).ToArray();
        for (var i = 0; i + 1 < ids.Length; i++)
        {
            Assert.True(
                ids[i].Order > ids[i + 1].Order,
                $"次序应当是 {tokens[i]}({ids[i].Order}) > {tokens[i + 1]}({ids[i + 1].Order})");
        }
    }

    [Fact]
    public void 主牌次序完全按文档第62行()
    {
        // "就是大王，小王，主5，副5，然后就是主花色的AKQJ～432，然后是其它花色的AKQ～432"
        AssertDescending(Trump, "大王", "小王", "♠5", "♥5", "♠A", "♠K", "♠2", "♥A", "♥2");
    }

    [Fact]
    public void 副级牌比主花色的A还大()
    {
        // 这条最反直觉：♣5 排在 ♠A 前面。文档第 62 行的列表就是这么写的。
        Assert.True(Id("♣5", Trump).Order > Id("♠A", Trump).Order);
        Assert.True(Id("♦5", Trump).Order > Id("♠K", Trump).Order);

        // 而且副 5 本身是主牌（文档第 63 行"所有其它花色 5 都是副主"），
        // 所以跟第 30 行"主对肯定比副对大"并不冲突。
        Assert.True(Id("♣5", Trump).IsTrump);
    }

    [Fact]
    public void 主5比副5大()
    {
        Assert.True(Id("♠5", Trump).Order > Id("♥5", Trump).Order);
        Assert.Equal(CardRole.MainLevel, Id("♠5", Trump).Role);
        Assert.Equal(CardRole.SubLevel, Id("♥5", Trump).Role);
    }

    [Fact]
    public void 不同花色的副级牌同样大()
    {
        // 文档第 63 行："如果主是黑桃5，那么所有其它花色5都是副主，等级相同，谁先出谁大。"
        Assert.Equal(Id("♥5", Trump).Order, Id("♣5", Trump).Order);
        Assert.Equal(Id("♣5", Trump).Order, Id("♦5", Trump).Order);
    }

    [Fact]
    public void 主花色内部按A到2而其它花色同级()
    {
        Assert.True(Id("♠A", Trump).Order > Id("♠K", Trump).Order);
        Assert.True(Id("♠K", Trump).Order > Id("♠2", Trump).Order);

        // 文档第 62 行"其它花色同级"：不同副花色之间只比点数。
        Assert.True(Id("♥A", Trump).Order > Id("♣K", Trump).Order);
        Assert.Equal(Id("♥K", Trump).Order, Id("♣K", Trump).Order);
    }

    [Fact]
    public void 跟牌分组看的是不是主牌而不是花色()
    {
        // 整个引擎最容易写错的地方：主黑桃打 5 时，♣5 是副主，
        // 它属于"主牌组"，**不属于梅花组**——别人出梅花时不用给出去。
        var clubFive = Id("♣5", Trump);
        Assert.Equal(Suit.Club, clubFive.Suit);            // 物理花色还是梅花（能和另一张 ♣5 成对）
        Assert.True(clubFive.IsTrump);
        Assert.Equal(FollowGroup.Trump, clubFive.Group);   // 但它已经被提成主牌了

        var clubNine = Id("♣9", Trump);
        Assert.Equal(Suit.Club, clubNine.Suit);
        Assert.False(clubNine.IsTrump);
        Assert.Equal(FollowGroup.Club, clubNine.Group);

        // 主花色的普通牌、主级牌、大小王，全都在主牌组。
        Assert.Equal(FollowGroup.Trump, Id("♠A", Trump).Group);
        Assert.Equal(FollowGroup.Trump, Id("♠5", Trump).Group);
        Assert.Equal(FollowGroup.Trump, Id("大王", Trump).Group);
        Assert.Equal(FollowGroup.Trump, Id("小王", Trump).Group);
    }

    [Fact]
    public void 小无主时所有花色级牌同级且只有王和级牌是主()
    {
        // 文档第 64 行："只有大小王和5，等级就是，大王，小王，所有等级牌，其它花色A～2"
        var ctx = TrumpContext.SmallNoTrump(Rank.Five);

        AssertDescending(ctx, "大王", "小王", "♠5", "♥A", "♠2");

        Assert.Equal(Id("♠5", ctx).Order, Id("♥5", ctx).Order);
        Assert.Equal(Id("♠5", ctx).Order, Id("♦5", ctx).Order);
        Assert.Equal(CardRole.LevelCard, Id("♠5", ctx).Role);

        Assert.True(Id("♠5", ctx).IsTrump);
        Assert.True(Id("♥5", ctx).IsTrump);
        Assert.False(Id("♠A", ctx).IsTrump);
    }

    [Fact]
    public void 大无主时级牌不再是主()
    {
        // 文档第 65 行："只有大小王是主，其它所有花色和当前等级牌都是副牌非主，
        // 等级是大王，小王，所有花色A～2，5已经不是主了，没有6大。"
        var ctx = TrumpContext.BigNoTrump(Rank.Five);

        AssertDescending(ctx, "大王", "小王", "♠A", "♠6", "♠5", "♠2");

        // 副牌之间同级：不同花色的同一张点数一样大。
        Assert.Equal(Id("♠A", ctx).Order, Id("♥A", ctx).Order);

        Assert.False(Id("♠5", ctx).IsTrump);
        Assert.False(Id("♥5", ctx).IsTrump);
        Assert.True(Id("♠5", ctx).Order < Id("♠6", ctx).Order);
    }

    [Fact]
    public void 拖拉机位次用自然序级牌不提权()
    {
        // 文档第 28 行："连对里面包含的即使主，也没有别的大，比如当前这局5是主，
        // 你打出4455，别人打出6677，你没有别人大。"
        Assert.Equal(Id("♠5", Trump).TractorRank, Id("♥5", Trump).TractorRank);
        Assert.Equal(Rank.Four.NaturalIndex(), Id("♠4", Trump).TractorRank);
        Assert.Equal(Rank.Five.NaturalIndex(), Id("♠5", Trump).TractorRank);

        // 大小王在自然序上接着 A，所以王对才能自成连对（文档第 65 行）。
        Assert.Equal(13, Id("小王", Trump).TractorRank);
        Assert.Equal(14, Id("大王", Trump).TractorRank);
    }

    [Fact]
    public void 牌力次序与自然序在位次上确实不同()
    {
        // 同一个 ♠5：比大小是仅次于王的第三档，拖拉机位次却只是"5"。
        var five = Id("♠5", Trump);
        Assert.Equal(TrumpOrder.MainLevel, five.Order);
        Assert.Equal(Rank.Five.NaturalIndex(), five.TractorRank);
    }
}
