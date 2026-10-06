namespace FanKou.Core.Cards;

/// <summary>
/// 外部卡牌样式的**文件名约定**。
///
/// 你自己做一套牌面图，丢进 skins 目录就能用；程序会按下面的名字去认。
/// 一张牌有多个可用的写法，认到哪个算哪个，都不认就用程序自带的画法。
///
/// 以黑桃 5 为例，下面这些名字**任意一个**都行：
/// <code>
///   ♠5.png    黑桃5.png    S5.png    spade5.png
/// </code>
/// 10 可以写 <c>♠10</c> 也可以写 <c>♠T</c>；大小王写 <c>大王.png</c> / <c>小王.png</c>，
/// 或者 <c>BigJoker.png</c> / <c>SmallJoker.png</c>；牌背写 <c>背面.png</c> 或 <c>back.png</c>。
///
/// 大小写不区分（Windows 上文件名本来就不区分），扩展名支持 png / jpg / bmp / gif。
/// </summary>
public static class CardFileNames
{
    /// <summary>支持的图片扩展名，按优先级排列。</summary>
    public static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>一张牌所有可能的名字（不含扩展名），按推荐程度从高到低。</summary>
    public static IReadOnlyList<string> ForCard(Card card)
    {
        var names = new List<string>();

        if (card.IsJoker)
        {
            var (jokerChinese, jokerLetter, jokerEnglish) = card.Rank == Rank.BigJoker
                ? ("大王", "BJ", "BigJoker")
                : ("小王", "SJ", "SmallJoker");

            names.Add(jokerChinese);
            names.Add(jokerEnglish);
            names.Add(jokerLetter);
            names.Add($"{jokerLetter}oker");
            names.Add(card.Rank == Rank.BigJoker ? "redjoker" : "blackjoker");
            return names;
        }

        var symbol = card.Suit.Symbol();          // ♠
        var chinese = SuitChinese(card.Suit);     // 黑桃
        var letter = SuitLetter(card.Suit);       // S
        var english = SuitEnglish(card.Suit);     // spade

        foreach (var rank in RankAliases(card.Rank))
        {
            names.Add($"{symbol}{rank}");         // ♠5
            names.Add($"{chinese}{rank}");        // 黑桃5
            names.Add($"{letter}{rank}");         // S5
            names.Add($"{english}{rank}");        // spade5
            names.Add($"{english}_{rank}");       // spade_5
        }

        return names;
    }

    /// <summary>推荐的文件名（做图的时候按这个命名最省事）。</summary>
    public static string Canonical(Card card) => card.IsJoker
        ? card.Rank.Text()
        : $"{card.Suit.Symbol()}{card.Rank.Text()}";

    /// <summary>牌背的可能名字。</summary>
    public static IReadOnlyList<string> ForBack() =>
        ["背面", "牌背", "back", "cardback", "card_back"];

    /// <summary>样式说明文件的名字。放了这个文件就能给样式起个显示名、写作者。</summary>
    public const string ManifestFileName = "skin.json";

    private static IEnumerable<string> RankAliases(Rank rank) => rank switch
    {
        Rank.Ten => ["10", "T"],
        Rank.Jack => ["J"],
        Rank.Queen => ["Q"],
        Rank.King => ["K"],
        Rank.Ace => ["A"],
        _ => [((int)rank).ToString()],
    };

    private static string SuitChinese(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方块",
        _ => string.Empty,
    };

    private static string SuitLetter(Suit suit) => suit switch
    {
        Suit.Spade => "S",
        Suit.Heart => "H",
        Suit.Club => "C",
        Suit.Diamond => "D",
        _ => string.Empty,
    };

    private static string SuitEnglish(Suit suit) => suit switch
    {
        Suit.Spade => "spade",
        Suit.Heart => "heart",
        Suit.Club => "club",
        Suit.Diamond => "diamond",
        _ => string.Empty,
    };
}
