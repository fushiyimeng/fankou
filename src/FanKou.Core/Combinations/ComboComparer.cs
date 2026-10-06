using FanKou.Core.Trump;

namespace FanKou.Core.Combinations;

/// <summary>
/// 两手同型同张数的牌比大小。**默认两手是在同一个领出语境下比较的**
/// （即已经开始判"谁能压住领出"），跨花色的胜负由 <c>TrickEvaluator</c> 负责。
/// </summary>
public static class ComboComparer
{
    /// <summary>a 压住 b 返回正数，被 b 压住返回负数，持平返回 0。</summary>
    public static int Compare(CardCombination a, CardCombination b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"张数不同的牌不能比大小：{a.Length} 张 vs {b.Length} 张", nameof(b));
        }

        if (a.Type != b.Type)
        {
            throw new ArgumentException($"牌型不同的牌不能比大小：{a.Type} vs {b.Type}", nameof(b));
        }

        if (a.IsTrump != b.IsTrump)
        {
            // 主牌永远压非主牌，这就是杀牌。
            return a.IsTrump ? 1 : -1;
        }

        return a.Key.CompareTo(b.Key);
    }

    public static bool Beats(CardCombination a, CardCombination b) => Compare(a, b) > 0;
}
