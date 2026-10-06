using FanKou.Core.Combinations;
using FanKou.Core.Trump;

namespace FanKou.Core.Play;

/// <summary>
/// 一墩牌里"谁最大"。只按出牌先后下标算，不关心座位——座位映射由上层负责。
/// </summary>
public static class TrickEvaluator
{
    /// <summary>比分值大一个数量级，保证"杀牌"和"同域"两档不会互相串味。</summary>
    private const long DomainSpan = 1_000_000;

    /// <summary>
    /// <paramref name="plays"/> 的下标 0 是领出方，依次跟牌。返回赢家的下标。
    /// 同大小时先出的赢（文档第 63 行"谁先出谁大"）。
    /// </summary>
    public static int WinnerIndex(IReadOnlyList<CardCombination> plays)
    {
        if (plays.Count == 0)
        {
            throw new ArgumentException("一墩牌里至少要有一次出牌", nameof(plays));
        }

        var lead = plays[0];
        var bestIndex = 0;
        var bestStrength = Strength(lead, lead);

        for (var i = 1; i < plays.Count; i++)
        {
            var strength = Strength(plays[i], lead);
            if (strength > bestStrength)
            {
                bestStrength = strength;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>候选牌能不能压住当前的领出。</summary>
    public static bool Beats(CardCombination candidate, CardCombination lead) =>
        Strength(candidate, lead) > Strength(lead, lead);

    /// <summary>
    /// 在同一个领出语境下，a 能不能压住 b。
    /// 给 AI 算"我这一手会不会被压住"用的——比如领出 ♣K 时，
    /// 外面还有几张 ♣ 能压它、或者有几张主能杀它。
    /// </summary>
    public static bool BeatsInContext(CardCombination a, CardCombination b, CardCombination lead) =>
        Strength(a, lead) > Strength(b, lead);

    /// <summary>
    /// 一手牌在指定领出下的战力。分三档：
    /// 0 = 垫牌，永远赢不了；1 域 = 跟同花色或同为主牌，按 Key 比；2 域 = 杀牌，永远赢非主。
    /// </summary>
    private static long Strength(CardCombination combo, CardCombination lead)
    {
        // 甩牌（散牌）是另一套规矩：张数对上，同花色散牌比大小，
        // 全主牌的可以直接杀（文档第 72 行"要杀的话，得全是主牌才可以"），
        // 其它花色的垫牌一律赢不了。
        if (lead.Type == ComboType.Throw)
        {
            if (combo.Length != lead.Length)
            {
                return 0;
            }

            if (combo.IsTrump)
            {
                return (2 * DomainSpan) + combo.Key;
            }

            return combo.Group == lead.Group ? DomainSpan + combo.Key : 0;
        }

        // 凑不成牌型的散牌永远赢不了。文档第 35 行："杀牌，只能说全主牌"——
        // 一张主牌混一张副牌既不是杀也不是对子，压不住任何东西。
        if (combo.Type == ComboType.Invalid)
        {
            return 0;
        }

        // 领出是对子/拖拉机时，跟的一方必须凑出**同样的结构**才谈得上比大小。
        // 比如领出连对，跟的人出了一堆同花色但连不起来的牌——那种牌会被认成
        // ComboType.Throw（甩牌只管认形状，认成什么跟"能不能赢"是两回事），
        // 它只是垫牌，一张都压不住。
        //
        // 这一条不能省，也不能指望下面的 Key 比较兜住：连对的 Key 是**自然序**
        // （TractorRank），对子/散牌的 Key 是**牌力次序**（Order，带 OffSuitBase
        // 那种千位基数），两者根本不是一个量纲。直接比会得出"♠7♠10♠J♠Q 压过
        // ♠K♠K♠A♠A 连对"这种荒唐结果（2026-10-06 用户实机撞到）。
        // 同理，ComboComparer 遇到牌型不同是直接抛异常的，说明这个前提本来就存在。
        if (lead.Type is ComboType.Pair or ComboType.Tractor && combo.Type != lead.Type)
        {
            return 0;
        }

        // 领出是副牌，我出的既不是这个花色、又不是主牌 —— 纯粹的垫牌。
        if (!lead.IsTrump && combo.Group != lead.Group && !combo.IsTrump)
        {
            return 0;
        }

        // 领出是主牌，我出副牌 —— 同样赢不了。
        if (lead.IsTrump && !combo.IsTrump)
        {
            return 0;
        }

        // 领出是副牌、我出主牌 —— 这是杀牌，稳赢同张数的任何非主牌。
        if (combo.IsTrump && !lead.IsTrump)
        {
            return 2 * DomainSpan + combo.Key;
        }

        return DomainSpan + combo.Key;
    }
}
