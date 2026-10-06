using FanKou.Core.Cards;

namespace FanKou.Core.Tests;

public class CardFileNamesTests
{
    private static bool Accepts(string card, string fileName) =>
        CardFileNames.ForCard(TestCards.One(card)).Contains(fileName);

    [Theory]
    [InlineData("♠5", "♠5")]
    [InlineData("♠5", "黑桃5")]
    [InlineData("♠5", "S5")]
    [InlineData("♠5", "spade5")]
    [InlineData("♥K", "♥K")]
    [InlineData("♥K", "红桃K")]
    [InlineData("♥K", "HK")]
    [InlineData("♣10", "♣10")]
    [InlineData("♣10", "♣T")]
    [InlineData("♣10", "club10")]
    [InlineData("♦A", "方块A")]
    [InlineData("♦A", "DA")]
    public void 常见的几种命名写法都认(string card, string fileName)
    {
        Assert.True(Accepts(card, fileName), $"{card} 应该认 {fileName}");
    }

    [Fact]
    public void 大小王的名字()
    {
        Assert.True(Accepts("大王", "大王"));
        Assert.True(Accepts("大王", "BigJoker"));
        Assert.True(Accepts("大王", "BJ"));
        Assert.True(Accepts("大王", "redjoker"));

        Assert.True(Accepts("小王", "小王"));
        Assert.True(Accepts("小王", "SmallJoker"));
        Assert.True(Accepts("小王", "SJ"));
        Assert.True(Accepts("小王", "blackjoker"));
    }

    [Fact]
    public void 牌背的名字()
    {
        var names = CardFileNames.ForBack();
        Assert.Contains("背面", names);
        Assert.Contains("back", names);
    }

    [Fact]
    public void 推荐文件名就是牌面文字()
    {
        Assert.Equal("♠5", CardFileNames.Canonical(TestCards.One("♠5")));
        Assert.Equal("大王", CardFileNames.Canonical(TestCards.One("大王")));
    }

    [Fact]
    public void 不会把花色和点数认错()
    {
        // ♠5 的名字里不该出现别的花色。
        var names = CardFileNames.ForCard(TestCards.One("♠5"));
        Assert.DoesNotContain("♥5", names);
        Assert.DoesNotContain("黑桃6", names);
    }
}
