using FanKou.Core.Cards;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

public class DeckTests
{
    [Fact]
    public void 整副牌共108张且每张唯一()
    {
        var deck = Deck.Create();

        Assert.Equal(108, deck.Length);
        Assert.Equal(108, deck.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void 每个花色26张两副各13张()
    {
        var deck = Deck.Create();

        foreach (var suit in Suits.All)
        {
            var ofSuit = deck.Where(c => c.Suit == suit).ToArray();
            Assert.Equal(26, ofSuit.Length);
            Assert.Equal(13, ofSuit.Select(c => c.Rank).Distinct().Count());
            Assert.All(ofSuit.GroupBy(c => c.Rank), g => Assert.Equal(2, g.Count()));
        }
    }

    [Fact]
    public void 大小王各两张()
    {
        var deck = Deck.Create();

        Assert.Equal(2, deck.Count(c => c.IsSmallJoker));
        Assert.Equal(2, deck.Count(c => c.IsBigJoker));
        Assert.All(deck.Where(c => c.IsJoker), c => Assert.Equal(Suit.Joker, c.Suit));
    }

    [Fact]
    public void 全副牌总分值恰好200分()
    {
        // 规则文档第 27 行：两副牌 8 个 5、8 个 10、8 个 K，一共 200 分。
        var deck = Deck.Create();

        Assert.Equal(8, deck.Count(c => c.Rank == Rank.Five));
        Assert.Equal(8, deck.Count(c => c.Rank == Rank.Ten));
        Assert.Equal(8, deck.Count(c => c.Rank == Rank.King));
        Assert.Equal(200, deck.Sum(c => c.Points));
    }

    [Fact]
    public void 洗牌不增不减只是换顺序()
    {
        var original = Deck.Create();
        var shuffled = Deck.Shuffle(original, new SeededRandomSource(12345));

        Assert.Equal(original.Length, shuffled.Length);
        Assert.Equal(
            original.OrderBy(c => c.Id).ToArray(),
            shuffled.OrderBy(c => c.Id).ToArray());
        Assert.NotEqual(original.ToArray(), shuffled.ToArray());
    }

    [Fact]
    public void 同seed洗出同一副牌()
    {
        var a = Deck.CreateShuffled(new SeededRandomSource(2026));
        var b = Deck.CreateShuffled(new SeededRandomSource(2026));
        var c = Deck.CreateShuffled(new SeededRandomSource(2027));

        Assert.Equal(a.ToArray(), b.ToArray());
        Assert.NotEqual(a.ToArray(), c.ToArray());
    }

    [Fact]
    public void 自然序与分值符合规则文档()
    {
        Assert.Equal(0, Rank.Two.NaturalIndex());
        Assert.Equal(12, Rank.Ace.NaturalIndex());
        Assert.Equal(13, Rank.SmallJoker.NaturalIndex());
        Assert.Equal(14, Rank.BigJoker.NaturalIndex());

        Assert.Equal(5, Rank.Five.Points());
        Assert.Equal(10, Rank.Ten.Points());
        Assert.Equal(10, Rank.King.Points());
        Assert.Equal(0, Rank.Ace.Points());
        Assert.Equal(0, Rank.SmallJoker.Points());
    }
}
