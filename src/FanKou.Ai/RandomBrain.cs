using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 只会挑合法解的 AI，完全不考虑得失。
/// 它的价值不在好玩，而在于把规则引擎逼到各种边角局面——
/// 上万个随机局跑下来不崩，才说明引擎真的稳。
/// </summary>
public sealed class RandomBrain(IRandomSource rng) : IPlayerBrain
{
    public CallOption? ChooseCall(CallContext context)
    {
        if (context.Options.Count == 0)
        {
            return null;
        }

        // 有牌必须叫：不能pass的时候一定要给一个叫法。
        if (!context.CanPass)
        {
            return context.Options[rng.Next(context.Options.Count)];
        }

        // 能pass时也偏向叫——随机局早点进入出牌阶段，覆盖率更高。
        return rng.Next(4) == 0 ? null : context.Options[rng.Next(context.Options.Count)];
    }

    public ImmutableArray<Card> ChooseBottomDiscards(BottomContext context)
    {
        var locked = context.Locked.Select(c => c.Id).ToHashSet();
        var candidates = context.Hand.Where(c => !locked.Contains(c.Id)).ToArray();

        if (candidates.Length < 8)
        {
            throw new InvalidOperationException(
                $"手上有 {context.Hand.Length} 张、其中明牌 {context.Locked.Length} 张，扣不出 8 张");
        }

        var picked = candidates.OrderBy(_ => rng.Next(int.MaxValue)).Take(8);
        return [.. picked];
    }

    public CardCombination ChoosePlay(PlayContext context)
    {
        if (context.Candidates.Count == 0)
        {
            throw new InvalidOperationException("没有合法出牌可选，规则引擎那边可能漏了候选");
        }

        return context.Candidates[rng.Next(context.Candidates.Count)];
    }
}
