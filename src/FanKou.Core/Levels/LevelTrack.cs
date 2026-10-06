using FanKou.Core.Cards;

namespace FanKou.Core.Levels;

/// <summary>
/// 双方的等级推进。规则文档（第三次补充第 1 条）给的完整序列是
/// 5 → 10 → K → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → J → Q → K → A，共 16 步，
/// 5/10/K 各出现两次、含义不同：前段不保级，后段保留等级。
///
/// 所以等级**必须存下标**，不能存 Rank——存 Rank 的话第二个 5 和第一个 5 就分不清了。
/// </summary>
public static class LevelTrack
{
    public static readonly Rank[] Sequence =
    [
        Rank.Five, Rank.Ten, Rank.King,                                     // 0..2   未过 K，输了掉回 5
        Rank.Two, Rank.Three, Rank.Four, Rank.Five,                         // 3..6   过 2，进入保级段
        Rank.Six, Rank.Seven, Rank.Eight, Rank.Nine,
        Rank.Ten, Rank.Jack, Rank.Queen, Rank.King, Rank.Ace,               // 11..15
    ];

    public const int Count = 16;

    /// <summary>下标 3 就是"2"。文档第 85 行："到 2 以后完全不掉等级了"。</summary>
    public const int ProtectionStart = 3;

    public const int AceIndex = 15;

    public static Rank RankAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return Sequence[index];
    }

    /// <summary>已过 2（含 2 本身），输牌不再掉级。</summary>
    public static bool IsProtected(int index) => index >= ProtectionStart;

    public static bool IsAce(int index) => index == AceIndex;

    /// <summary>晋级。打到 A 就封顶，不会溢出。</summary>
    public static int Advance(int index, int levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(levels);
        return Math.Min(index + levels, AceIndex);
    }

    /// <summary>
    /// 掉级。这是"败方没有等级保护就变成 5"的**唯一**实现点，
    /// 别处不要再写 `= 0`，否则漏掉保护判断只是时间问题。
    /// </summary>
    public static int Demote(int index) => IsProtected(index) ? index : 0;

    /// <summary>显示用，例如 "K(已过2)" 这类场景可以靠它判断。</summary>
    public static string Describe(int index) => $"{RankAt(index).Text()}";
}
