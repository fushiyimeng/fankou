using FanKou.Core.Cards;
using FanKou.Core.Combinations;

namespace FanKou.Core.Scoring;

/// <summary>底牌结算的结论。</summary>
/// <param name="Points">底牌里本来有多少分。</param>
/// <param name="Multiplier">倍率，看最后一手的牌型。</param>
/// <param name="Counted">庄家赢下最后一手时，分数作废，只翻开展示。</param>
public sealed record BottomResult(int Points, int Multiplier, bool Counted)
{
    public int Score => Counted ? Points * Multiplier : 0;

    public string Describe() => Counted
        ? $"底牌 {Points} 分 × {Multiplier} 倍 = {Score} 分，计入闲家"
        : $"底牌 {Points} 分，但庄家赢下最后一手，作废（只翻开展示）";
}

public static class BottomSettlement
{
    /// <summary>
    /// 结算底牌。倍率只看**最后一手**的牌型，跟底牌本身是什么无关（文档第 58 行）。
    /// 而且必须闲家赢下最后一手才计分（文档第 22 行）。
    /// </summary>
    public static BottomResult Settle(
        IReadOnlyList<Card> bottom,
        ComboType lastTrickWinningType,
        bool defenderWonLastTrick)
    {
        var points = bottom.Sum(c => c.Points);
        var multiplier = ScoreRules.MultiplierFor(lastTrickWinningType);

        return new BottomResult(points, multiplier, defenderWonLastTrick);
    }
}
