using FanKou.Ai;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Trump;

namespace FanKou.Core.Tests;

/// <summary>记牌：还没露过面的牌有哪些。AI 的"记牌"就是记这个。</summary>
public class CardMemoryTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static TrickPlay Play(Seat seat, string cards)
    {
        var parsed = TestCards.Parse(cards);
        var combo = ComboRecognizer.TryRecognize(parsed, Trump, out var recognized)
            ? recognized!
            : CardCombination.Shapeless(parsed, false, parsed[0].Suit);

        return new TrickPlay(seat, combo);
    }

    private static PublicBoardInfo Info(
        IReadOnlyList<TrickRecord>? completed = null,
        IReadOnlyList<Card>? shown = null) =>
        new(
            [25, 25, 25, 25],
            [],
            completed ?? [],
            shown ?? [],
            Team.EastWest,
            Seat.East,
            0,
            0,
            1);

    [Fact]
    public void 手上和已经打过的牌都算露过面()
    {
        var hand = TestCards.Parse("♠A ♥K");
        var trick = new TrickRecord(1, Seat.East, [Play(Seat.East, "♣9")], 0, true);

        var memory = CardMemory.Build(hand, Info([trick]));

        Assert.True(memory.HasAppeared(TestCards.One("♠A")));     // 自己手上
        Assert.True(memory.HasAppeared(TestCards.One("♣9")));     // 打出去过
        Assert.True(memory.HasAppeared(TestCards.One("♥K")));

        // 剩下来没露面的 = 108 - 2（手上）- 1（打过的）
        Assert.Equal(105, memory.UnseenCount);
    }

    [Fact]
    public void 叫主摆出来的明牌也是公开信息()
    {
        var hand = TestCards.Parse("♠A");
        var shown = TestCards.Parse("小王 ♠5");

        var memory = CardMemory.Build(hand, Info(shown: shown));

        Assert.True(memory.HasAppeared(TestCards.One("小王")));
        Assert.True(memory.HasAppeared(TestCards.One("♠5")));
        Assert.Equal(108 - 1 - 2, memory.UnseenCount);
    }

    [Fact]
    public void 没露过面的分牌分数算得对()
    {
        var memory = CardMemory.Build([], Info());

        // 一张牌都没露面的时候，整副 200 分都在外面
        Assert.Equal(200, memory.UnseenPoints);
    }

    [Fact]
    public void 最大的牌外面没人压得住()
    {
        var hand = TestCards.Parse("大王");
        var memory = CardMemory.Build(hand, Info());

        var combo = ComboRecognizer.TryRecognize(TestCards.Parse("大王"), Trump, out var recognized)
            ? recognized!
            : throw new InvalidOperationException();

        // 外面还有一张大王，但一样大不算压得住
        Assert.Equal(0, memory.UnseenBeating(combo, combo, Trump));
    }

    [Fact]
    public void 小牌外面一堆能压的()
    {
        var memory = CardMemory.Build([], Info());

        var combo = ComboRecognizer.TryRecognize(TestCards.Parse("♠3"), Trump, out var recognized)
            ? recognized!
            : throw new InvalidOperationException();

        // 主黑桃打 5 时，一张 ♠3 上面：♠4~♠A、三张副 5、大小王，一堆都能压
        Assert.True(memory.UnseenBeating(combo, combo, Trump) > 20);
    }

    [Fact]
    public void 主牌外面一张都没有的时候领单张是安全的()
    {
        // 把 108 张里除了 ♠3 以外的牌全"露过面"，那领 ♠3 就没人压得住
        var seen = Deck.Create().Where(c => c.Id != TestCards.One("♠3").Id).ToArray();

        // 用一大堆"打过的牌"来冒充，够表达意思了
        var plays = seen.Select((c, i) => new TrickPlay(
            Seats.All[i % 4],
            CardCombination.Shapeless([c], false, c.Suit))).ToArray();

        var info = new PublicBoardInfo(
            [25, 25, 25, 25],
            plays,
            [],
            [],
            Team.EastWest,
            Seat.East,
            0,
            0,
            1);

        var memory = CardMemory.Build([], info);
        var combo = ComboRecognizer.TryRecognize(TestCards.Parse("♠3"), Trump, out var recognized)
            ? recognized!
            : throw new InvalidOperationException();

        Assert.Equal(0, memory.UnseenBeating(combo, combo, Trump));
    }
}

/// <summary>推算：谁空门了。这是从"该给的花色没给够"硬推出来的，不是猜的。</summary>
public class OpponentModelTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static TrickPlay Play(Seat seat, string cards)
    {
        var parsed = TestCards.Parse(cards);
        var combo = ComboRecognizer.TryRecognize(parsed, Trump, out var recognized)
            ? recognized!
            : CardCombination.Shapeless(parsed, false, parsed[0].Suit);

        return new TrickPlay(seat, combo);
    }

    private static OpponentModel Build(IReadOnlyList<TrickPlay> plays, bool completed = true)
    {
        var info = completed
            ? new PublicBoardInfo([0, 0, 0, 0], [], [new TrickRecord(1, Seat.East, [.. plays], 0, true)], [], Team.EastWest, Seat.East, 0, 0, 1)
            : new PublicBoardInfo([0, 0, 0, 0], [.. plays], [], [], Team.EastWest, Seat.East, 0, 0, 1);

        return OpponentModel.Build(info, Trump);
    }

    [Fact]
    public void 该给的花色没给够就是空门了()
    {
        var model = Build([
            Play(Seat.East, "♣K ♣K"),      // 领出梅花对
            Play(Seat.South, "♣9 ♦3"),     // 只给出一张梅花 —— 他梅花空了
            Play(Seat.West, "♣A ♣Q"),      // 给够了，不能断定
            Play(Seat.North, "♣7 ♣8"),
        ]);

        Assert.True(model.IsVoid(Seat.South, FollowGroup.Club));
        Assert.False(model.IsVoid(Seat.West, FollowGroup.Club));
        Assert.False(model.IsVoid(Seat.North, FollowGroup.Club));

        // 空了梅花、主牌还没见底，那就可能拿主牌来杀
        Assert.True(model.MightRuff(Seat.South, FollowGroup.Club));
    }

    [Fact]
    public void 给够了就不能说人家空门()
    {
        var model = Build([
            Play(Seat.East, "♣K"),
            Play(Seat.South, "♣2"),
            Play(Seat.West, "♣3"),
            Play(Seat.North, "♣4"),
        ]);

        Assert.Equal(0, model.VoidCount(FollowGroup.Club));
    }

    [Fact]
    public void 一张都没给就是彻底空门()
    {
        var model = Build([
            Play(Seat.East, "♣K ♣K"),
            Play(Seat.South, "♦3 ♦4"),     // 一张梅花都没给
            Play(Seat.West, "♣A ♣Q"),
            Play(Seat.North, "♣7 ♣8"),
        ]);

        Assert.True(model.IsVoid(Seat.South, FollowGroup.Club));
        Assert.Equal(1, model.VoidCount(FollowGroup.Club));
    }

    [Fact]
    public void 正在打的这一轮也算数()
    {
        // 本轮还没打完，但已经出过的人照样能推
        var model = Build(
            [
                Play(Seat.East, "♣K ♣K"),
                Play(Seat.South, "♣9 ♦3"),
            ],
            completed: false);

        Assert.True(model.IsVoid(Seat.South, FollowGroup.Club));
    }

    [Fact]
    public void 领出的人自己不算空门()
    {
        var model = Build([
            Play(Seat.East, "♣K ♣K"),
            Play(Seat.South, "♣9 ♣8"),
        ]);

        Assert.False(model.IsVoid(Seat.East, FollowGroup.Club));
    }
}
