using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

public class ComboRecognizerTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static CardCombination Combo(string text, TrumpContext? context = null)
    {
        var cards = TestCards.Parse(text);
        Assert.True(
            ComboRecognizer.TryRecognize(cards, context ?? Trump, out var combo, out var reason),
            reason);
        return combo!;
    }

    private static string Reject(string text, TrumpContext? context = null)
    {
        var cards = TestCards.Parse(text);
        Assert.False(
            ComboRecognizer.TryRecognize(cards, context ?? Trump, out _, out var reason),
            $"{text} 本来不该被认成合法牌型");
        return reason;
    }

    [Fact]
    public void 单张就是对子以外的任何一张()
    {
        var combo = Combo("♥9");
        Assert.Equal(ComboType.Single, combo.Type);
        Assert.False(combo.IsTrump);
        Assert.Equal(Suit.Heart, combo.Group);

        var trump = Combo("♠9");
        Assert.True(trump.IsTrump);
    }

    [Fact]
    public void 对子必须同花色同点数()
    {
        Assert.Equal(ComboType.Pair, Combo("♣K ♣K").Type);

        Assert.Contains("不是对子", Reject("♠5 ♥5"));   // 同为 5，但花色不同
        Assert.Contains("不是对子", Reject("♣K ♣Q"));
    }

    [Fact]
    public void 王对也算对子()
    {
        // 文档第 24 行："对子必须同花色，但两大王/两小王也算。"
        Assert.Equal(ComboType.Pair, Combo("小王 小王").Type);
        Assert.Equal(ComboType.Pair, Combo("大王 大王").Type);
        Assert.True(Combo("小王 小王").IsTrump);

        // 一张小王一张大王不是对子。
        Assert.Contains("不是对子", Reject("小王 大王"));
    }

    [Fact]
    public void 连对必须是挨着的两个同花色对子()
    {
        var tractor = Combo("♣7 ♣7 ♣8 ♣8");
        Assert.Equal(ComboType.Tractor, tractor.Type);
        Assert.Equal(2, tractor.PairCount);
        Assert.Equal(Suit.Club, tractor.Group);

        // 不挨着的两对不是拖拉机 —— 但它可以当甩牌出（前提是这门里没人大得过）
        Assert.Equal(ComboType.Throw, Combo("♣4 ♣4 ♣7 ♣7").Type);

        // 三张同点一张别的，凑不成对，也是甩牌
        Assert.Equal(ComboType.Throw, Combo("♣4 ♣4 ♣4 ♣6").Type);

        // 但混了主牌就不行：打 5 时 ♣5 是主牌，跟 ♣4 不是一门
        Assert.Contains("同属一门", Reject("♣4 ♣4 ♣4 ♣5"));
    }

    [Fact]
    public void 三连对和更长的连对都成立()
    {
        var three = Combo("♣7 ♣7 ♣8 ♣8 ♣9 ♣9");
        Assert.Equal(ComboType.Tractor, three.Type);
        Assert.Equal(3, three.PairCount);
        Assert.Equal(6, three.Length);
    }

    [Fact]
    public void 王对能不能自成连对取决于开关()
    {
        // 文档第 65 行"两对王可以租拖拉机"，第 89 行用户又标注待问长辈。
        var on = Combo("小王 小王 大王 大王");
        Assert.Equal(ComboType.Tractor, on.Type);
        Assert.Equal(2, on.PairCount);
        Assert.Equal(Rank.BigJoker.NaturalIndex(), on.Key);
        Assert.True(on.IsTrump);

        var off = RuleOptions.Default with { JokerPairChainIsTractor = false };
        var ctx = TrumpContext.Suited(Suit.Spade, Rank.Five, off);
        Assert.Contains("rules.json", Reject("小王 小王 大王 大王", ctx));
    }

    [Fact]
    public void 拖拉机必须同一花色副主不能凑进去()
    {
        // 文档第 98 行（2026-10-06 补充）："如果主是黑桃5，那么梅花5红心5方块5等也是主，
        // 但属于副主……不算是花色的牌了，但即使是主，在打拖拉机的也不可以用。
        // 因为拖拉机的规则是花色一样的，比如现在是黑桃5主，你拿黑桃44和梅花55就不可以，
        // 必须是黑桃4455才算拖拉机。"
        Assert.Contains("同一花色", Reject("♠4 ♠4 ♣5 ♣5"));

        // 副主夹在副花色的连对里同样不行——♣5 归主牌组，♣6 归梅花组，两头不靠。
        Assert.Contains("混了主牌和副牌", Reject("♣5 ♣5 ♣6 ♣6"));

        // 主花色自己的连对才是合法的。
        Assert.Equal(ComboType.Tractor, Combo("♠4 ♠4 ♠5 ♠5").Type);
    }

    [Fact]
    public void 三连对里王对不能参与()
    {
        // 文档第 67 行："如果是三连对，那么大小王就无法用了，
        // 比如 445566，你需要 778899 以上可以压住，两大王+两小王再加AA都不算。"
        Assert.Contains("同一花色", Reject("小王 小王 大王 大王 ♠A ♠A"));

        // 而纯花色的三连对本身是完全合法的。
        Assert.Equal(ComboType.Tractor, Combo("♠4 ♠4 ♠5 ♠5 ♠6 ♠6").Type);
    }

    [Fact]
    public void 副花色连对里不能夹副级牌()
    {
        // 主黑桃打 5 时 ♣5 是副主，它根本不算梅花，
        // 所以 ♣4455 是"一张主牌夹在副花色里"，两头不靠，成不了连对。
        Assert.Contains("混了主牌和副牌", Reject("♣4 ♣4 ♣5 ♣5"));

        // 主花色自己的连对没这个问题——那一组本来就全是主牌。
        // 文档第 28 行"连对里面包含的即使主"说的就是这种。
        var trumpTractor = Combo("♠4 ♠4 ♠5 ♠5");
        Assert.Equal(ComboType.Tractor, trumpTractor.Type);
        Assert.True(trumpTractor.IsTrump);
    }

    [Fact]
    public void 张数不支持的牌型会给出原因()
    {
        // ♣AKK 不是对子 —— 它按甩牌算（能不能甩得出去另说，那是出牌时判的）
        Assert.Equal(ComboType.Throw, Combo("♣A ♣K ♣K").Type);

        // 五张这种奇数张不认
        Assert.Contains("5 张", Reject("♣4 ♣4 ♣5 ♣5 ♣6"));
    }
}

public class ComboComparerTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static CardCombination Combo(string text) =>
        TestCards.Parse(text) is var cards
        && ComboRecognizer.TryRecognize(cards, Trump, out var combo, out _)
            ? combo!
            : throw new InvalidOperationException($"{text} 不是合法牌型");

    [Fact]
    public void 张数不同不能比()
    {
        var single = Combo("♠9");
        var pair = Combo("♠9 ♠9");

        Assert.Throws<ArgumentException>(() => ComboComparer.Compare(single, pair));
    }

    [Fact]
    public void 主牌压非主牌()
    {
        // 文档第 30 行："主对肯定比副对大啊。"
        Assert.True(ComboComparer.Beats(Combo("♥5 ♥5"), Combo("♠A ♠A")));
        Assert.True(ComboComparer.Beats(Combo("♠2 ♠2"), Combo("♥A ♥A")));
    }

    [Fact]
    public void 同为副牌时比其他大小()
    {
        Assert.True(ComboComparer.Beats(Combo("♥A ♥A"), Combo("♥K ♥K")));
        Assert.False(ComboComparer.Beats(Combo("♥K ♥K"), Combo("♥A ♥A")));
    }

    [Fact]
    public void 单打一对级牌时AA也压不住()
    {
        // 文档第 28 行："如果是单打一对55，那么AA也没有你大。"
        Assert.True(ComboComparer.Beats(Combo("♠5 ♠5"), Combo("♠A ♠A")));
    }

    [Fact]
    public void 连对只按连对比较级牌不提权()
    {
        // 文档第 67 行："比如当前主是5，4455没有5566大，因为拖拉机规则里，只认拖拉机。"
        Assert.True(ComboComparer.Beats(Combo("♠5 ♠5 ♠6 ♠6"), Combo("♠4 ♠4 ♠5 ♠5")));
        Assert.True(ComboComparer.Beats(Combo("♠6 ♠6 ♠7 ♠7"), Combo("♠4 ♠4 ♠5 ♠5")));

        // 但 A 打头的连对仍然压得住 6 打头的，因为都按自然序。
        Assert.True(ComboComparer.Beats(Combo("♠K ♠K ♠A ♠A"), Combo("♠6 ♠6 ♠7 ♠7")));
    }

    [Fact]
    public void 副主对子很大但只有本主对子和王对压得住()
    {
        // 文档第 98 行："单打对子可以，比如梅花55，依旧是很大的牌，
        // 出得是本主对子以上的才可以压住，比如黑桃55，俩大小王对子都可以。"
        var subLevelPair = Combo("♣5 ♣5");
        Assert.Equal(ComboType.Pair, subLevelPair.Type);
        Assert.True(subLevelPair.IsTrump);

        // 主花色的 A 对压不住副主对。
        Assert.True(ComboComparer.Beats(subLevelPair, Combo("♠A ♠A")));

        // 本主对子、小王对、大王对都压得住。
        Assert.True(ComboComparer.Beats(Combo("♠5 ♠5"), subLevelPair));
        Assert.True(ComboComparer.Beats(Combo("小王 小王"), subLevelPair));
        Assert.True(ComboComparer.Beats(Combo("大王 大王"), subLevelPair));
    }

    [Fact]
    public void 主连对杀副连对()
    {
        // 文档第 68 行："你叫牌梅花KKAA，拖拉机……但他刚好黑桃里有2233，那么他大，因为是他杀的你。"
        Assert.True(ComboComparer.Beats(Combo("♠2 ♠2 ♠3 ♠3"), Combo("♣K ♣K ♣A ♣A")));
    }
}
