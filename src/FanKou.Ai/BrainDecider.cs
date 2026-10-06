using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Trump;

namespace FanKou.Ai;

/// <summary>
/// 把"某个座位现在该决策什么"翻译成"该提交哪条命令"。
/// 无界面批量自打和带界面的对局共用这一份，免得两边的规则跑偏。
/// </summary>
public static class BrainDecider
{
    public static GameCommand Decide(
        IPlayerBrain brain,
        PendingDecision pending,
        TrumpContext? trump,
        PublicBoardInfo info)
    {
        switch (pending)
        {
            case PendingDecision.NeedCall needCall:
            {
                var chosen = brain.ChooseCall(new CallContext(
                    needCall.Actor,
                    needCall.Hand,
                    needCall.Options,
                    needCall.CanPass,
                    needCall.LevelRank,
                    needCall.IsCounter));

                if (chosen is null)
                {
                    if (!needCall.CanPass)
                    {
                        throw new InvalidOperationException(
                            $"{needCall.Actor.Name()} 家有牌必须叫，却给了个不叫");
                    }

                    return new PassCall(needCall.Actor);
                }

                if (!needCall.Options.Contains(chosen))
                {
                    throw new InvalidOperationException("脑子给了一个不在候选里的叫法");
                }

                return new DeclareCall(needCall.Actor, chosen);
            }

            case PendingDecision.NeedBottomSwap swap:
            {
                // 交给脑子的是"手上 25 张 + 锅底 8 张"，跟真人能挑的范围一致。
                var discards = brain.ChooseBottomDiscards(new BottomContext(
                    swap.Actor, swap.Pool, swap.Locked, Require(trump)));

                if (discards.Length != 8)
                {
                    throw new InvalidOperationException($"换底牌必须扣 8 张，脑子给了 {discards.Length} 张");
                }

                return new CommitBottom(swap.Actor, discards);
            }

            case PendingDecision.NeedPlay play:
            {
                var context = new PlayContext(
                    play.Actor, play.Hand, play.Lead, play.Candidates, Require(trump), info);

                return new PlayCards(play.Actor, brain.ChoosePlay(context).Cards);
            }

            default:
                throw new NotSupportedException($"未知的待决策：{pending.GetType().Name}");
        }
    }

    /// <summary>
    /// 从引擎读一份公开信息——**不含**别家手牌。
    /// 叫主摆出来的明牌算公开信息，要给出去。
    /// <paramref name="actor"/> 是正在做决策的那家，用来判断他能不能看到锅底。
    /// </summary>
    public static PublicBoardInfo Describe(GameEngine engine, Seat actor) => new(
        [.. Seats.All.Select(seat => engine.HandOf(seat).Count)],
        [.. engine.CurrentTrick],
        [.. engine.History],
        [.. engine.Locked],
        engine.DeclarerTeam,
        engine.DeclarerSeat,
        engine.DefenderTrickPoints,
        engine.DeclarerTrickPoints,
        engine.TrickNumber)
    {
        Bottom = KnownBottom(engine, actor),
    };

    /// <summary>
    /// 庄家看得到锅底，因为那 8 张是他自己挑的。但**必须是他真正拿过之后**才算数——
    /// 首局的反主发生在换底牌**之前**（文档第 88 行），那会儿锅底还没人动过，
    /// 泄漏出去就是作弊。所以只在出牌阶段及以后给。
    /// </summary>
    private static IReadOnlyList<Card> KnownBottom(GameEngine engine, Seat actor)
    {
        if (!engine.HasDeclarer || engine.DeclarerSeat != actor)
        {
            return [];
        }

        return engine.Phase is GamePhase.Playing or GamePhase.Settling or GamePhase.RoundOver
            ? [.. engine.Bottom]
            : [];
    }

    private static TrumpContext Require(TrumpContext? trump) =>
        trump ?? throw new InvalidOperationException("还没定主就开始出牌了");
}
