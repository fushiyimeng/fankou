using FanKou.Core.Combinations;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>几档 AI 共用的出牌打分零件。</summary>
internal static class PlayScoring
{
    /// <summary>这一手出去能不能拿到本墩。</summary>
    public static bool WouldWin(CardCombination candidate, PlayContext context)
    {
        var trick = context.Info.CurrentTrick;
        if (trick.Count == 0)
        {
            return false;
        }

        var plays = new List<CardCombination>(trick.Count + 1);
        foreach (var play in trick)
        {
            plays.Add(play.Combo);
        }

        plays.Add(candidate);
        return Core.Play.TrickEvaluator.WinnerIndex(plays) == plays.Count - 1;
    }

    /// <summary>出这一手要付出多大代价。牌越大越贵，动主牌更贵。</summary>
    public static double Cost(CardCombination combo, TrumpContext trump)
    {
        var order = combo.Cards.Max(c => IdentityResolver.Resolve(c, trump).Order);
        var cost = order * 0.004;

        if (combo.IsTrump)
        {
            cost += 7;
        }

        if (combo.Type == ComboType.Tractor)
        {
            cost += 4;
        }

        return cost;
    }

    /// <summary>在候选里挑分最高的，加一点随机扰动，免得同一个局面每次都走一样的牌。</summary>
    public static CardCombination PickBest(
        PlayContext context,
        IRandomSource rng,
        Func<CardCombination, double> score)
    {
        var best = default(CardCombination);
        var bestValue = double.NegativeInfinity;

        foreach (var candidate in context.Candidates)
        {
            var value = score(candidate) + (rng.NextDouble() * 0.01);
            if (value > bestValue)
            {
                bestValue = value;
                best = candidate;
            }
        }

        return best ?? context.Candidates[0];
    }
}
