using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Play;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

/// <summary>
/// 跟牌规则。每一条都对应规则文档里的一段原文，注释里标了出处。
/// </summary>
public class FollowRuleTests
{
    /// <summary>主黑桃、打 5。文档里的例子基本都建立在这个设定上。</summary>
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static CardCombination Lead(string text)
    {
        Assert.True(
            ComboRecognizer.TryRecognize(TestCards.Parse(text), Trump, out var combo, out var reason),
            reason);
        return combo!;
    }

    private static ValidationResult Follow(string lead, string hand, string selected)
    {
        var cards = TestCards.Parse(hand);
        var chosen = TestCards.Parse(selected);
        return MoveValidator.Validate(Lead(lead), cards, chosen, Trump);
    }

    private static void Allow(string lead, string hand, string selected)
    {
        var result = Follow(lead, hand, selected);
        Assert.True(result.IsValid, $"「{selected}」本应合法，却被拒：{result.Reason}");
    }

    private static void Forbid(string lead, string hand, string selected, string expectedReason)
    {
        var result = Follow(lead, hand, selected);
        Assert.False(result.IsValid, $"「{selected}」本应非法，却被放过");
        Assert.Contains(expectedReason, result.Reason);
    }

    [Fact]
    public void 张数必须和领出方一致()
    {
        // 文档第 36 行："不管什么情况下，出牌的出了几张，那么其它几家也必须出一样张数的牌。"
        Forbid("♣4 ♣4", "♣9 ♣8 ♥3", "♣9", "2 张");
    }

    [Fact]
    public void 有对子必须出对子哪怕拆连对()
    {
        // 文档第 76 行："如果对手出了对子，你手上有对子，那么必须出对子，
        // 比如黑桃对6，你有黑桃连对7788，那么你都必须拆掉连对，出黑桃77或者88。"
        const string hand = "♣7 ♣7 ♣8 ♣8";

        Allow("♣6 ♣6", hand, "♣7 ♣7");
        Allow("♣6 ♣6", hand, "♣8 ♣8");
        Forbid("♣6 ♣6", hand, "♣7 ♣8", "必须出对子");
    }

    [Fact]
    public void 手上没有对子时可以出两张散牌()
    {
        Allow("♣6 ♣6", "♣7 ♣9 ♣J", "♣7 ♣9");
    }

    [Fact]
    public void 只有一张该花色时不用垫主牌()
    {
        // 文档第 102 行："自己手上有一张同花色非主的情况下，可以不用给主牌。"
        // 第 69 行那条"只有一张梅花也必须再给一张主"是同一处的早期说法，已被第 102 行作废。
        const string hand = "♣9 ♥3 ♠2";

        Allow("♣4 ♣4", hand, "♣9 ♠2");
        Allow("♣4 ♣4", hand, "♣9 ♥3");
    }

    [Fact]
    public void 副级牌算主牌不算原来的花色()
    {
        // 主黑桃打 5 时，♣5 是副主，它**不算梅花**——
        // 所以别人出梅花时不用给出去，手上就算只有 ♣5 也等于没有梅花。
        // 落到"空门必须垫主"上：这副牌里能当梅花使的是 0 张，两张牌得全出主牌，
        // 所以只有 ♣5 配 ♠2（都是主）走得通。
        const string voidHand = "♣5 ♠2 ♥3";

        Allow("♣6 ♣6", voidHand, "♣5 ♠2");
        Forbid("♣6 ♣6", voidHand, "♣5 ♥3", "必须用主牌垫");
        Forbid("♣6 ♣6", voidHand, "♠2 ♥3", "必须用主牌垫");

        // 但如果手上另有真正的梅花，那就只是"有但凑不齐"，另一张随便补。
        const string hand = "♣9 ♣5 ♠2 ♥3";

        Allow("♣6 ♣6", hand, "♣9 ♠2");
        Allow("♣6 ♣6", hand, "♣9 ♣5");
        Allow("♣6 ♣6", hand, "♣9 ♥3");
    }

    [Fact]
    public void 完全没这个花色时必须垫主牌()
    {
        // 文档第 102 行："但一张都没有的情况下，就必须垫主牌。"
        // 这条推翻了第 31 行"没有梅花可以杀也可以垫"——那句管的是领出单张，
        // 见 领出单张时空门依然随便出()。
        const string hand = "♥9 ♦3 ♠2 ♠2";

        Allow("♣4 ♣4", hand, "♠2 ♠2");                        // 全主，合法
        Forbid("♣4 ♣4", hand, "♥9 ♦3", "必须用主牌垫");        // 一张主都没有
        Forbid("♣4 ♣4", hand, "♥9 ♠2", "必须用主牌垫");        // 只垫了一张，不够
    }

    [Fact]
    public void 空门时主牌不够也要尽量垫出去()
    {
        // 一张主牌也没有：实在垫不出来，只能随便出，不能把人卡死。
        Allow("♣4 ♣4", "♥9 ♦3", "♥9 ♦3");

        // 只有一张主牌却要垫两张：那一张得白搭进去（文档第 69 行
        // "哪怕你的主只剩下大王了，依旧需要白搭进入"的用意），剩下一张才随便。
        const string hand = "♠2 ♥9 ♦3";

        Allow("♣4 ♣4", hand, "♠2 ♥9");
        Allow("♣4 ♣4", hand, "♠2 ♦3");
        Forbid("♣4 ♣4", hand, "♥9 ♦3", "必须用主牌垫");
    }

    [Fact]
    public void 领出单张时空门依然随便出()
    {
        // 空门必须垫主只管"对子/拖拉机"。领出单张时文档第 31 行说的是
        // "可以选择出其它花色的，也可以杀，或者垫"，这条没被推翻。
        const string hand = "♥9 ♦3 ♠2";

        Allow("♣4", hand, "♥9");
        Allow("♣4", hand, "♦3");
        Allow("♣4", hand, "♠2");
    }

    [Fact]
    public void 空门时生成器只给主牌候选()
    {
        // 校验器和生成器必须同进同退，否则"提示"按钮轮播出来的牌
        // 点下去会被"出牌"按钮拒掉。
        var cards = TestCards.Parse("♥9 ♦3 ♠2 ♠2");
        var candidates = MoveGenerator.GenerateFollows(Lead("♣4 ♣4"), [.. cards], Trump);

        Assert.NotEmpty(candidates);
        Assert.All(
            candidates,
            candidate => Assert.All(
                candidate.Cards,
                card => Assert.True(
                    IdentityResolver.Resolve(card, Trump).IsTrump,
                    $"{card} 不是主牌，不该出现在空门的候选里")));
    }

    [Fact]
    public void 有连对就必须出连对()
    {
        // 文档第 90 行："有对子出对子，有连队出连队。这个对子规则和拖拉机规则。甩牌不算。"
        // 领出用 ♣7788 而不是 ♣4455——打 5 时 ♣5 已经是主牌，凑不成梅花连对。
        const string hand = "♣9 ♣9 ♣10 ♣10 ♣J ♣J";

        Allow("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣9 ♣10 ♣10");
        Allow("♣7 ♣7 ♣8 ♣8", hand, "♣10 ♣10 ♣J ♣J");
        Forbid("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣9 ♣J ♣J", "必须出连对");
    }

    [Fact]
    public void 只有一对时要配上散牌凑够张数()
    {
        // 文档第 76 行："如果是对手出了拖拉机，如果你只有一个对子，
        // 你也必须出一对子，在加两张牌筹够相同张数。"
        const string hand = "♣9 ♣9 ♣J ♣Q ♣K";

        Allow("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣9 ♣J ♣Q");
        Forbid("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣J ♣Q ♣K", "先出对子");
    }

    [Fact]
    public void 连对的缺口不用垫主牌()
    {
        // 手上三张梅花（含一对）要跟四张的拖拉机。有该花色就够豁免了，
        // 缺口那一张随便补（文档第 102 行）。
        const string hand = "♣9 ♣9 ♣J ♠2 ♥3";

        Allow("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣9 ♣J ♠2");
        Allow("♣7 ♣7 ♣8 ♣8", hand, "♣9 ♣9 ♣J ♥3");
    }

    [Fact]
    public void 领出单张时只要给一张同花色就行()
    {
        Allow("♣4", "♣9 ♣8 ♥3", "♣8");
        Forbid("♣4", "♣9 ♣8 ♥3", "♥3", "必须出");
    }

    [Fact]
    public void 出的牌必须在手上()
    {
        Forbid("♣4 ♣4", "♣7 ♣8 ♥3", "♣9 ♣10", "不在自己手上");
    }
}
