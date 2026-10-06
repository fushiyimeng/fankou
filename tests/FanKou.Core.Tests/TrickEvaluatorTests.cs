using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Play;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

public class TrickEvaluatorTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    /// <summary>大无主：只有大小王是主，级牌不再是主（文档第 65 行）。</summary>
    private static readonly TrumpContext NoTrump = TrumpContext.BigNoTrump(Rank.Ten);

    private static CardCombination Combo(string text) => Combo(text, Trump);

    private static CardCombination Combo(string text, TrumpContext context) =>
        ComboRecognizer.TryRecognize(TestCards.Parse(text), context, out var combo, out var reason)
            ? combo!
            : throw new InvalidOperationException($"{text} 不是合法牌型：{reason}");

    [Fact]
    public void 同花色的散牌压不住连对()
    {
        // 用户 2026-10-06 实机撞到的：大无主下南家领 ♠K♠K♠A♠A 连对，
        // 北家跟了一手同花色但连不起来的 ♠7♠10♠J♠Q，居然判北家最大。
        //
        // 根因是 TrickEvaluator 直接拿两边的 Key 比大小，而这两种牌型的 Key
        // **不是一个量纲**：连对的 Key 是自然序（TractorRank，A 约 12），
        // 散牌的 Key 是牌力次序（Order = OffSuitBase 1000 + 自然序）。
        // 1000+10 当然大于 12，散牌就这么"赢"了。
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♠K ♠K ♠A ♠A", NoTrump),   // 领出：连对
            Combo("♠7 ♠10 ♠J ♠Q", NoTrump),  // 同花色但凑不出连对，纯垫牌
            Combo("♠7 ♠8 ♠9 ♠J", NoTrump),   // 同上
        ]);

        Assert.Equal(0, winner);
    }

    [Fact]
    public void 同花色的散牌压不住对子()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♥K ♥K"),   // 领出：红桃对子（主黑桃打 5）
            Combo("♣K ♣K"),   // 别门的对子，垫牌
        ]);

        Assert.Equal(0, winner);
    }

    [Fact]
    public void 没人压得住时领出的赢()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♥A"),        // 领出
            Combo("♦K"),        // 既不是红桃也不是主，垫牌
            Combo("♣9"),
            Combo("♦3"),
        ]);

        Assert.Equal(0, winner);
    }

    [Fact]
    public void 同花色最大的赢()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♥K"),
            Combo("♥9"),
            Combo("♥A"),
            Combo("♥Q"),
        ]);

        Assert.Equal(2, winner);
    }

    [Fact]
    public void 主牌杀得掉副牌()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♥A"),
            Combo("♠3"),        // 黑桃是主，哪怕只是一张 3 也杀得住
            Combo("♥K"),
            Combo("♦A"),
        ]);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void 副级牌是主牌可以拿去杀()
    {
        // 主黑桃打 5 时 ♣5 是副主。手上没梅花时，它可以当主牌去杀别人的梅花。
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♣A"),        // 领出梅花
            Combo("♣5"),        // ♣5 是副主，这一手算杀
            Combo("♣K"),
            Combo("♦5"),        // 同样是副主，但后出
        ]);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void 主局里副牌永远赢不了()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♠2"),        // 领出主牌
            Combo("♥A"),        // 红桃 A 再大也是副牌
            Combo("♠A"),        // 主花色的 A 才是真的大
            Combo("♦A"),
        ]);

        Assert.Equal(2, winner);
    }

    [Fact]
    public void 同大的副级牌先手取胜()
    {
        // 文档第 63 行："所有其它花色5都是副主，等级相同，谁先出谁大。"
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♠4"),        // 领出：主牌小牌
            Combo("♥5"),        // 副级牌，此刻是主牌里最大的一档，反超
            Combo("♣5"),        // 与 ♥5 同级，但后出
            Combo("♦5"),        // 同样同级
        ]);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void 杀牌对杀牌时更大的杀牌赢()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♥A"),
            Combo("♠3"),        // 杀
            Combo("♦2"),
            Combo("♠K"),        // 也是杀，而且比 ♠3 大
        ]);

        Assert.Equal(3, winner);
    }

    [Fact]
    public void 领出主牌时副级牌能压过主花色的A()
    {
        // 文档第 62 行的次序：副5 排在主花色 AKQJ…2 之前。
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♠A"),
            Combo("♦5"),
            Combo("♠K"),
            Combo("♥3"),
        ]);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void 连对之间同样是主杀副()
    {
        var winner = TrickEvaluator.WinnerIndex([
            Combo("♣K ♣K ♣A ♣A"),
            Combo("♦2 ♦2 ♦3 ♦3"),
            Combo("♠2 ♠2 ♠3 ♠3"),   // 主连对，杀
            Combo("♥9 ♥9 ♥10 ♥10"),
        ]);

        Assert.Equal(2, winner);
    }

    [Fact]
    public void 张数不同的一墩没有意义()
    {
        // 合法性由 MoveValidator 保证；这里只确认比较器不会悄悄放过。
        var single = Combo("♥A");
        var pair = Combo("♥K ♥K");

        Assert.Throws<ArgumentException>(() => ComboComparer.Compare(single, pair));
    }
}
