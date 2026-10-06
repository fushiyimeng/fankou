using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

public class CardSorterTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Heart, Rank.Five);

    [Fact]
    public void 主牌排最前面其余按花色从大到小()
    {
        var hand = TestCards.Parse(
            "♦4 ♣K ♥A ♠10 ♥5 ♣2 ♦9 ♥K ♠5 ♠5 小王 大王");

        var sorted = CardSorter.ForDisplay(hand, Trump);

        var expected = TestCards.Parse(
            "大王 小王 ♥5 ♠5 ♠5 ♥A ♥K ♠10 ♣K ♣2 ♦9 ♦4");

        Assert.Equal(
            expected.Select(c => CardText.Format(c)).ToArray(),
            sorted.Select(c => CardText.Format(c)).ToArray());
    }

    [Fact]
    public void 副级牌排在自己的花色之前()
    {
        // 主红桃打 5 时，♠5 是副主，它要排在 ♥A 前面（文档第 62 行的次序）。
        var hand = TestCards.Parse("♥A ♥K ♠5");

        var sorted = CardSorter.ForDisplay(hand, Trump);

        Assert.Equal("♠5", CardText.Format(sorted[0]));
        Assert.Equal("♥A", CardText.Format(sorted[1]));
    }

    [Fact]
    public void 主牌里的王排最前()
    {
        var hand = TestCards.Parse("♥A ♥5 大王 小王 ♠A");

        var sorted = CardSorter.ForDisplay(hand, Trump);

        Assert.Equal("大王", CardText.Format(sorted[0]));
        Assert.Equal("小王", CardText.Format(sorted[1]));
        Assert.Equal("♥5", CardText.Format(sorted[2]));
    }

    [Fact]
    public void 没有主牌的时候按花色排()
    {
        var hand = TestCards.Parse("♦A ♣A ♥A ♠A");

        var sorted = CardSorter.ForDisplay(hand, null);

        Assert.Equal(
            new[] { "♠A", "♥A", "♣A", "♦A" },
            sorted.Select(c => CardText.Format(c)).ToArray());
    }
}
