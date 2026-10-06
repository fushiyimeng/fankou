using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Play;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

/// <summary>
/// 甩牌：同花色的几张散牌一起甩出去。
///
/// 规矩（文档第 27、72 行）：必须同花色；而且**这门里别人手上没有比你更大的牌**才能甩。
/// 例外的"顶牌"（打 5 是 AAK、打 K 是 AAQ）永远甩得出去 ——
/// 两副牌每门只有两张 A，谁也凑不出三张来压它。
/// </summary>
public class ThrowTests
{
    private static readonly TrumpContext TrumpFive = TrumpContext.Suited(Suit.Spade, Rank.Five);
    private static readonly TrumpContext TrumpKing = TrumpContext.Suited(Suit.Spade, Rank.King);

    private static CardCombination Combo(string text, TrumpContext context)
    {
        Assert.True(
            ComboRecognizer.TryRecognize(TestCards.Parse(text), context, out var combo, out var reason),
            reason);
        return combo!;
    }

    private static string Reject(string text, TrumpContext context)
    {
        Assert.False(
            ComboRecognizer.TryRecognize(TestCards.Parse(text), context, out _, out var reason),
            $"{text} 本来不该被认成合法牌型");
        return reason;
    }

    private static bool Legal(string text, string others, TrumpContext context) =>
        ThrowRules.IsLegal(Combo(text, context), TestCards.Parse(others), context);

    // ---------------------------------------------------------- 顶牌 AAK / AAQ

    [Fact]
    public void 打5的时候这门顶牌是AAK()
    {
        var (top, next) = ComboRecognizer.TopTripleRanks(Suit.Club, TrumpFive);
        Assert.Equal(Rank.Ace, top);
        Assert.Equal(Rank.King, next);

        var combo = Combo("♣A ♣A ♣K", TrumpFive);
        Assert.Equal(ComboType.Throw, combo.Type);   // 甩牌，不是对子
        Assert.Equal(3, combo.Length);
    }

    [Fact]
    public void 打K的时候顶牌变成AAQ()
    {
        // K 被提成主牌、从梅花里拿掉了，所以梅花的次大变成 Q。
        var (top, next) = ComboRecognizer.TopTripleRanks(Suit.Club, TrumpKing);
        Assert.Equal(Rank.Ace, top);
        Assert.Equal(Rank.Queen, next);
    }

    [Fact]
    public void 大无主下级牌不是主牌所以不摘()
    {
        // 大无主里 5 已经不是主牌了，梅花该有的牌一张不少，顶牌还是 AAK
        var (top, next) = ComboRecognizer.TopTripleRanks(Suit.Club, TrumpContext.BigNoTrump(Rank.Five));

        Assert.Equal(Rank.Ace, top);
        Assert.Equal(Rank.King, next);
    }

    [Fact]
    public void 顶牌永远甩得出去()
    {
        // 两副牌每门只有两张 A，别人凑不出更大的 —— 就算别家手上还有梅花的牌也一样
        var others = "♣K ♣Q ♥4 ♦9 ♠2 ♣J";

        Assert.True(Legal("♣A ♣A ♣K", others, TrumpFive));
        Assert.True(Legal("♣A ♣A ♣Q", others, TrumpKing));
    }

    // ---------------------------------------------------------- 一般甩牌

    [Fact]
    public void 别人手上有更大的就甩不出去()
    {
        // 我甩 ♣KQJ，最小的 J 都被人压得住
        Assert.False(Legal("♣K ♣Q ♣J", "♣A ♥4 ♦9", TrumpFive));

        // 我手上拿着两张 ♣A 才甩得出去
        Assert.True(Legal("♣A ♣A ♣K", "♣K ♥4 ♦9", TrumpFive));
    }

    [Fact]
    public void 只有别人手上更大的才算()
    {
        // 那张 ♣A 已经打出去了、或者本来就在锅底 —— 都压不到我
        Assert.True(Legal("♣K ♣Q ♣J", "♥4 ♦9 ♠2", TrumpFive));
    }

    [Fact]
    public void 甩牌必须同一花色()
    {
        Assert.Contains("同一花色", Reject("♣A ♣A ♥K", TrumpFive));
    }

    [Fact]
    public void 关掉开关就不认甩牌了()
    {
        var options = RuleOptions.Default with { AllowThrows = false };
        var context = TrumpContext.Suited(Suit.Spade, Rank.Five, options);

        Assert.Contains("rules.json", Reject("♣A ♣A ♣K", context));

        // 拖拉机和对子不受影响
        Assert.Equal(ComboType.Pair, Combo("♣A ♣A", context).Type);
        Assert.Equal(ComboType.Tractor, Combo("♣7 ♣7 ♣8 ♣8", context).Type);
    }

    // ---------------------------------------------------------- 出牌与跟牌

    [Fact]
    public void 甩牌能当领出打出去()
    {
        var hand = TestCards.Parse("♣A ♣A ♣K ♥3 ♥4").ToImmutableArray();
        var others = TestCards.Parse("♣K ♥9 ♦2 ♠3");

        var leads = MoveGenerator.GenerateLeads(hand, TrumpFive, others);

        Assert.Contains(leads, l => l.Type == ComboType.Throw && l.Group == Suit.Club);
    }

    [Fact]
    public void 甩不出去的不会出现在候选里()
    {
        // 手上只有 ♣KQJ，外面还有 ♣A —— 这一手甩不出去，不该给出来
        var hand = TestCards.Parse("♣K ♣Q ♣J ♥3 ♥4").ToImmutableArray();
        var others = TestCards.Parse("♣A ♥9 ♦2 ♠3");

        var leads = MoveGenerator.GenerateLeads(hand, TrumpFive, others);

        Assert.DoesNotContain(leads, l => l.Type == ComboType.Throw);
    }

    [Fact]
    public void 跟甩牌凑够张数就行不用垫主()
    {
        var lead = Combo("♣A ♣A ♣K", TrumpFive);
        var hand = TestCards.Parse("♣9 ♣8 ♥4 ♦3 ♠2");

        var byHeart = MoveValidator.Validate(lead, hand, TestCards.Parse("♣9 ♣8 ♥4"), TrumpFive);
        var byDiamond = MoveValidator.Validate(lead, hand, TestCards.Parse("♣9 ♣8 ♦3"), TrumpFive);

        Assert.True(byHeart.IsValid, byHeart.Reason);
        Assert.True(byDiamond.IsValid, byDiamond.Reason);

        // 有该花色还是得先给
        Assert.False(MoveValidator.Validate(lead, hand, TestCards.Parse("♣9 ♥4 ♦3"), TrumpFive).IsValid);
    }

    [Fact]
    public void 甩牌只能被同张数的主牌杀()
    {
        var lead = Combo("♣A ♣A ♣K", TrumpFive);

        var ruff = CardCombination.Shapeless(
            TestCards.Parse("♠2 ♠3 ♠4"), isTrump: true, group: Suit.Spade, key: 5002);
        Assert.True(TrickEvaluator.Beats(ruff, lead));

        var dump = CardCombination.Shapeless(
            TestCards.Parse("♦9 ♦8 ♦7"), isTrump: false, group: Suit.Diamond);
        Assert.False(TrickEvaluator.Beats(dump, lead));

        Assert.Equal(1, TrickEvaluator.WinnerIndex([lead, ruff, dump, dump]));
        Assert.Equal(0, TrickEvaluator.WinnerIndex([lead, dump, dump, dump]));
    }

    [Fact]
    public void 底牌倍率按甩牌算两倍()
    {
        Assert.Equal(2, FanKou.Core.Scoring.ScoreRules.MultiplierFor(ComboType.Throw));
    }
}
