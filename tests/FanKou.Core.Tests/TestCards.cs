using FanKou.Core.Cards;

namespace FanKou.Core.Tests;

/// <summary>
/// 把 "♠5 ♠5 ♥K 大王" 这样的文本解析成牌，让测试用例能直接抄规则文档里的牌面。
/// 同名牌按出现顺序分配 Copy（0/1），所以 "♠5 ♠5" 得到的是两副牌里的那两张 ♠5。
/// </summary>
internal static class TestCards
{
    public static Card[] Parse(string text)
    {
        var copies = new Dictionary<(Suit, Rank), byte>();
        var result = new List<Card>();

        foreach (var token in text.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Identify(token);
            var copy = copies.TryGetValue(key, out var used) ? (byte)(used + 1) : (byte)0;
            copies[key] = copy;
            result.Add(new Card(key.Item1, key.Item2, copy));
        }

        return [.. result];
    }

    /// <summary>只取一张牌，方便写"这一手是 ♠5"这类断言。</summary>
    public static Card One(string token) => Parse(token)[0];

    private static (Suit, Rank) Identify(string token) => token switch
    {
        "大王" => (Suit.Joker, Rank.BigJoker),
        "小王" => (Suit.Joker, Rank.SmallJoker),
        _ => (SymbolToSuit(token[0]), TextToRank(token[1..])),
    };

    private static Suit SymbolToSuit(char symbol) => symbol switch
    {
        '♠' or 'S' or 's' => Suit.Spade,
        '♥' or 'H' or 'h' => Suit.Heart,
        '♣' or 'C' or 'c' => Suit.Club,
        '♦' or 'D' or 'd' => Suit.Diamond,
        _ => throw new ArgumentException($"无法识别的花色：{symbol}", nameof(symbol)),
    };

    private static Rank TextToRank(string text) => text.ToUpperInvariant() switch
    {
        "2" => Rank.Two,
        "3" => Rank.Three,
        "4" => Rank.Four,
        "5" => Rank.Five,
        "6" => Rank.Six,
        "7" => Rank.Seven,
        "8" => Rank.Eight,
        "9" => Rank.Nine,
        "10" or "T" => Rank.Ten,
        "J" => Rank.Jack,
        "Q" => Rank.Queen,
        "K" => Rank.King,
        "A" => Rank.Ace,
        _ => throw new ArgumentException($"无法识别的点数：{text}", nameof(text)),
    };
}
