using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Trump;
using FanKou.Ui.Controls;

namespace FanKou.App;

/// <summary>
/// 造一个假局面，纯粹为了把界面渲染成图片检查布局用（--render-ui）。
/// 跟真实对局没关系，正式跑的时候用不到。
/// </summary>
internal static class DemoState
{
    public static TrumpContext Trump { get; } = TrumpContext.Suited(Suit.Heart, Rank.Five);

    public static IReadOnlyList<Card> Hand { get; } =
    [
        new Card(Suit.Joker, Rank.BigJoker, 0),
        new Card(Suit.Joker, Rank.SmallJoker, 0),
        new Card(Suit.Heart, Rank.Five, 0),
        new Card(Suit.Heart, Rank.Ace, 0),
        new Card(Suit.Heart, Rank.King, 0),
        new Card(Suit.Spade, Rank.Five, 0),
        new Card(Suit.Spade, Rank.Five, 1),
        new Card(Suit.Spade, Rank.Ten, 0),
        new Card(Suit.Club, Rank.King, 0),
        new Card(Suit.Club, Rank.Two, 0),
        new Card(Suit.Diamond, Rank.Nine, 0),
        new Card(Suit.Diamond, Rank.Four, 0),
    ];

    /// <param name="withTrick">false 表示"刚叫完主、还没开打"那个瞬间，用来看明牌摊开的样子。</param>
    /// <summary>开发用：造一份"手上 25 张 + 锅底 8 张"的数据，渲染换底牌对话框用。</summary>
    public static (IReadOnlyList<Card> Hand, IReadOnlyList<Card> Bottom, IReadOnlyList<Card> Locked) SwapSample()
    {
        var deck = Deck.CreateShuffled(new FanKou.Core.Util.SeededRandomSource(99));
        var hand = deck.Take(25).ToArray();
        var bottom = deck.Skip(25).Take(8).ToArray();

        // 假装叫主用的是这一手里的两张（定主）
        var locked = new[] { hand[4], hand[9] };

        return (hand, bottom, locked);
    }

    public static TableSnapshot Snapshot(bool withTrick = true)
    {
        TrickPlay[] plays = withTrick
            ?
            [
                Play(Seat.East, "♣K"),
                Play(Seat.South, "♣A"),
                Play(Seat.West, "♥3"),
                Play(Seat.North, "♣9"),
            ]
            : [];

        return new TableSnapshot
        {
            Trick = plays,
            HumanSeat = Seat.South,
            TrumpText = Trump.Describe(),
            LevelText = "5（东西）",
            DeclarerTeam = Team.EastWest,
            DeclarerSeat = Seat.West,
            DefenderPoints = 75,
            DeclarerPoints = 85,
            RemainingCounts = [18, 12, 17, 19],
            TrickNumber = 7,
            BottomRevealed = false,
            ShownCards = [new Card(Suit.Spade, Rank.Five, 0), new Card(Suit.Spade, Rank.Five, 1), new Card(Suit.Joker, Rank.SmallJoker, 0)],
            ShownCardsSeat = Seat.West,
            Status = "轮到你跟牌：这一手是 单张 ♣K，要出 1 张",
        };
    }

    private static TrickPlay Play(Seat seat, string cards)
    {
        var parsed = Parse(cards);
        return ComboRecognizer.TryRecognize(parsed, Trump, out var combo)
            ? new TrickPlay(seat, combo!)
            : throw new InvalidOperationException($"{cards} 不是合法牌型");
    }

    private static Card[] Parse(string text)
    {
        var result = new List<Card>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var suit = token[0] switch
            {
                '♠' => Suit.Spade,
                '♥' => Suit.Heart,
                '♣' => Suit.Club,
                '♦' => Suit.Diamond,
                _ => Suit.Joker,
            };

            var rankText = token[1..];
            var rank = rankText switch
            {
                "A" => Rank.Ace,
                "K" => Rank.King,
                "Q" => Rank.Queen,
                "J" => Rank.Jack,
                "10" => Rank.Ten,
                _ => (Rank)int.Parse(rankText),
            };

            result.Add(new Card(suit, rank, 0));
        }

        return [.. result];
    }
}
