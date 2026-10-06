namespace FanKou.Core.Cards;

/// <summary>牌面文字的唯一样式出口，界面和日志都走这里，避免各处自己拼字符串拼得不一致。</summary>
public static class CardText
{
    public static string Symbol(this Suit suit) => suit switch
    {
        Suit.Spade => "♠",
        Suit.Heart => "♥",
        Suit.Club => "♣",
        Suit.Diamond => "♦",
        _ => string.Empty,
    };

    public static string Text(this Rank rank) => rank switch
    {
        Rank.Two => "2",
        Rank.Three => "3",
        Rank.Four => "4",
        Rank.Five => "5",
        Rank.Six => "6",
        Rank.Seven => "7",
        Rank.Eight => "8",
        Rank.Nine => "9",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        Rank.SmallJoker => "小王",
        Rank.BigJoker => "大王",
        _ => "?",
    };

    /// <summary>例如 "♠5"、"大王"。两副牌的同名牌在这里看不出区别，界面另加小圆点区分。</summary>
    public static string Format(Card card) =>
        card.Suit == Suit.Joker ? card.Rank.Text() : card.Suit.Symbol() + card.Rank.Text();

    public static string Format(IEnumerable<Card> cards) => string.Join(" ", cards.Select(Format));

    public static string Format(Card[] cards) => string.Join(" ", cards.Select(Format));
}
