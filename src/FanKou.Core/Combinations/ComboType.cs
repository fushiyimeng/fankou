namespace FanKou.Core.Combinations;

public enum ComboType : byte
{
    /// <summary>不成型、或第一版还不支持的牌型（甩牌）。</summary>
    Invalid = 0,

    Single = 1,
    Pair = 2,

    /// <summary>同花色连对，最少两连对，可以更长。</summary>
    Tractor = 3,

    /// <summary>甩牌。第一版不实现，留到第二轮。</summary>
    Throw = 4,
}
