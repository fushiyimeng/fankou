namespace FanKou.Core.Cards;

/// <summary>
/// 花色。Joker 不是真正的花色，只是为了让大小王也能参与分组（Group）运算而存在：
/// 两张小王自成一组、两张大王自成一组。
/// </summary>
public enum Suit : byte
{
    Spade = 0,
    Heart = 1,
    Club = 2,
    Diamond = 3,
    Joker = 4,
}

public static class Suits
{
    /// <summary>四种真正的花色，不含 Joker。</summary>
    public static readonly Suit[] All = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];

    /// <summary>黑色花色（配小王）——规则文档第 12 条：小王配黑桃或梅花。</summary>
    public static readonly Suit[] Black = [Suit.Spade, Suit.Club];

    /// <summary>红色花色（配大王）——规则文档第 12 条：大王配红桃或方块。</summary>
    public static readonly Suit[] Red = [Suit.Heart, Suit.Diamond];

    public static bool IsBlack(this Suit s) => s is Suit.Spade or Suit.Club;

    public static bool IsRed(this Suit s) => s is Suit.Heart or Suit.Diamond;

    /// <summary>不是 Joker 的才是真花色。</summary>
    public static bool IsReal(this Suit s) => s != Suit.Joker;
}
