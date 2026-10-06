using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 换底牌。文档第 22 行说得很清楚：锅底 8 张要**拿上来**换，然后丢掉 8 张。
/// 所以能挑的范围是手上 25 张加锅底 8 张，一共 33 张。
/// </summary>
public class BottomSwapTests
{
    /// <summary>跑到"轮到换底牌"那一步，返回引擎。</summary>
    private static (GameEngine Engine, PendingDecision.NeedBottomSwap Swap) ReachBottomSwap(int seed)
    {
        var engine = new GameEngine(new SeededRandomSource(seed));
        engine.StartRound();

        // 首局比普通局多一段反主（发牌途中叫主 → 反主 → 才拿底牌），
        // 所以这里统一处理：叫得出就叫，叫不起就过，一路推到换底牌为止。
        var guard = 0;
        while (guard++ < 3000)
        {
            switch (engine.Pending)
            {
                case PendingDecision.NeedBottomSwap swap:
                    return (engine, swap);

                case PendingDecision.NeedCall call:
                    engine.Apply(call.Options.Count > 0
                        ? new DeclareCall(call.Actor, call.Options[0])
                        : new PassCall(call.Actor));
                    continue;
            }

            if (engine.Phase == GamePhase.Dealing)
            {
                engine.AdvanceDeal();
                continue;
            }

            break;
        }

        throw new InvalidOperationException($"种子 {seed} 没能走到换底牌");
    }

    [Fact]
    public void 换底牌的时候能挑的范围是33张()
    {
        var (_, swap) = ReachBottomSwap(3);

        Assert.Equal(25, swap.Hand.Length);
        Assert.Equal(8, swap.Bottom.Length);
        Assert.Equal(33, swap.Pool.Length);

        // 手上和锅底不能有重复的牌
        Assert.Equal(33, swap.Pool.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void 可以扣掉锅底拿上来的那几张()
    {
        var (engine, swap) = ReachBottomSwap(3);

        // 故意扣 8 张**全部来自锅底**——老实现是从手上挑，这种情况根本做不到。
        var discards = swap.Bottom.ToArray();
        Assert.Equal(8, discards.Length);

        engine.Apply(new CommitBottom(swap.Actor, [.. discards]));

        Assert.Equal(GamePhase.Playing, engine.Phase);
        Assert.Equal(25, engine.HandOf(swap.Actor).Count);

        // 扣出去的 8 张成了新的锅底
        Assert.Equal(
            discards.Select(c => c.Id).Order().ToArray(),
            engine.Bottom.Select(c => c.Id).Order().ToArray());

        // 手上剩下的恰好是原来那 25 张
        Assert.Equal(
            swap.Hand.Select(c => c.Id).Order().ToArray(),
            engine.HandOf(swap.Actor).Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public void 混合着扣也成立()
    {
        var (engine, swap) = ReachBottomSwap(5);

        // 锅底 4 张 + 手上 4 张
        var discards = swap.Bottom.Take(4).Concat(swap.Hand.Take(4)).ToArray();
        engine.Apply(new CommitBottom(swap.Actor, [.. discards]));

        Assert.Equal(25, engine.HandOf(swap.Actor).Count);
        Assert.Equal(8, engine.Bottom.Count);

        // 一张牌只能在一个地方
        var all = engine.HandOf(swap.Actor).Concat(engine.Bottom).Select(c => c.Id).ToArray();
        Assert.Equal(33, all.Distinct().Count());
    }

    [Fact]
    public void 明牌不能扣()
    {
        var (engine, swap) = ReachBottomSwap(3);
        Assert.NotEmpty(swap.Locked);

        var discards = swap.Locked.Take(1)
            .Concat(swap.Pool.Where(c => swap.Locked.All(l => l.Id != c.Id)).Take(7))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            engine.Apply(new CommitBottom(swap.Actor, [.. discards])));
    }

    [Fact]
    public void 张数不对不给扣()
    {
        var (engine, swap) = ReachBottomSwap(3);

        Assert.Throws<InvalidOperationException>(() =>
            engine.Apply(new CommitBottom(swap.Actor, [.. swap.Pool.Take(7)])));
    }

    [Fact]
    public void 不在33张里的牌不给扣()
    {
        var (engine, swap) = ReachBottomSwap(3);

        // 找一张不在这 33 张里的
        var outside = Deck.Create().First(c => swap.Pool.All(p => p.Id != c.Id));
        var discards = swap.Pool.Take(7).Append(outside).ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            engine.Apply(new CommitBottom(swap.Actor, [.. discards])));
    }
}
