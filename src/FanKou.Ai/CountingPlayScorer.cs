using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Play;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Ai;

/// <summary>
/// 会记牌的出牌逻辑，"普通"和"困难"两档共用，区别只在于要不要用推算（<c>useInference</c>）。
///
/// 比"简单"档多知道两件事：
/// <list type="bullet">
/// <item>这一手外面还有几手能压它 —— 从没露过面的牌里数出来</item>
/// <item>这些都压不压得住，值不值得动大牌</item>
/// </list>
/// "困难"档再多一层：把"外面有几手能压"折算成概率——那些牌还有一部分可能躺在锅底里，
/// 不一定在对家手上，所以真实威胁要打个折。
/// </summary>
internal static class CountingPlayScorer
{
    public static CardCombination Choose(PlayContext context, IRandomSource rng, bool useInference)
    {
        var memory = CardMemory.Build(context.Hand, context.Info);

        // 空门推算只给困难档用。试过给普通档也开，结果更差——
        // 领牌时老想着"别送给人杀"会过度保守，该冲的时候不冲。
        var model = useInference ? OpponentModel.Build(context.Info, context.Trump) : null;

        return context.IsLeading
            ? ChooseLead(context, rng, memory, model)
            : ChooseFollow(context, rng, memory, model, useInference);
    }

    /// <summary>
    /// 跟牌。这里**不用加权打分**，而是走一套明确的取舍顺序——
    /// 之前试过把"安全"折成分数加进去，结果它贪图每一墩都稳赢，
    /// 把大牌浪费在小墩上，真到有分的大墩反而没牌压了，比不记牌还差。
    ///
    /// 记牌在这里的用处是：知道"哪些赢法是真的稳"，以及"台面这几分值不值得动大牌"。
    /// </summary>
    private static CardCombination ChooseFollow(
        PlayContext context,
        IRandomSource rng,
        CardMemory memory,
        OpponentModel? model,
        bool useInference)
    {
        var trump = context.Trump;
        var candidates = context.Candidates;
        var tablePoints = context.Info.CurrentTrick.Sum(play => play.Combo.Points);

        var partnerWinning = context.Info.CurrentTrickWinner is { } winner
            && winner.TeamOf() == context.Seat.TeamOf();

        if (partnerWinning)
        {
            // 队友已经最大了：有分就垫给他，同时别浪费大牌。
            return candidates
                .OrderByDescending(c => c.Points)
                .ThenBy(c => PlayScoring.Cost(c, trump))
                .First();
        }

        var winners = candidates
            .Where(c => PlayScoring.WouldWin(c, context))
            .Select(c => (Combo: c, Threat: ThreatOf(c, context, memory, model)))
            .ToArray();

        if (winners.Length == 0)
        {
            // 压不住：别送分出去，顺手把最没用的牌走掉。
            return candidates
                .OrderBy(c => c.Points)
                .ThenBy(c => PlayScoring.Cost(c, trump))
                .First();
        }

        if (tablePoints > 0)
        {
            // 台面有分，值得求稳：挑外面最压不住的那一手，同样稳就挑便宜的。
            return winners
                .OrderBy(x => x.Threat)
                .ThenBy(x => PlayScoring.Cost(x.Combo, trump))
                .First()
                .Combo;
        }

        // 台面没分，不值得为这一墩花大牌 —— 便宜优先，稳不稳其次。
        // 试过"没分的墩就干脆让了、把牌留着捡分"，结果更差：
        // 能赢的墩还是该赢，赢了才有下一轮的先手，让出去反而被动。
        return winners
            .OrderBy(x => PlayScoring.Cost(x.Combo, trump))
            .First()
            .Combo;
    }

    /// <summary>
    /// 这一手外面**预计**还有几手能压。
    ///
    /// 用 <see cref="CardMemory.ExpectedBeating"/> 而不是原始张数——锅底那 8 张
    /// 谁也打不出来，算成威胁会让 AI 虚惊，而且越到残局虚高得越厉害。
    /// 困难档另外还会用空门推断剔掉假威胁：对头要是都没主牌了，外面剩的主就杀不到我。
    /// </summary>
    private static double ThreatOf(
        CardCombination candidate,
        PlayContext context,
        CardMemory memory,
        OpponentModel? model)
    {
        var countTrumps = model is null || !OpponentsOutOfTrumps(context, model);

        return memory.ExpectedBeating(candidate, context.Lead!, context.Trump, countTrumps);
    }

    /// <summary>
    /// 两家对头是不是都被证实没有主牌了。
    /// 这是硬推断：某家该给主牌的时候没给够，他就是没主了——
    /// 一旦两家都空了，外面剩下的主只可能在队友手上或者锅里，杀不到我。
    /// </summary>
    private static bool OpponentsOutOfTrumps(PlayContext context, OpponentModel model) =>
        Seats.All
            .Where(seat => seat.TeamOf() != context.Seat.TeamOf())
            .All(seat => model.IsVoid(seat, FollowGroup.Trump));

    private static CardCombination ChooseLead(
        PlayContext context,
        IRandomSource rng,
        CardMemory memory,
        OpponentModel? model)
    {
        var trump = context.Trump;
        var hand = context.Hand;
        var trumpCount = hand.Count(c => IdentityResolver.Resolve(c, trump).IsTrump);
        var iAmDeclarer = context.Seat.TeamOf() == context.Info.DeclarerTeam;
        var trumpsOut = memory.UnseenTrumps(trump);

        return PlayScoring.PickBest(context, rng, candidate =>
        {
            // 基础判断跟简单档一样，记牌只**追加**信息，不推翻原来的取向。
            var value = candidate.Cards.Length * 1.5;
            value -= candidate.Points * 1.6;
            value -= PlayScoring.Cost(candidate, trump) * 0.6;
            value += hand.Count(c => c.Suit == candidate.Group) * 1.2;

            // 坐庄主牌多就先抽主牌，但外面已经没主了就别抽了。
            if (iAmDeclarer && trumpCount >= 7 && trumpsOut > 0)
            {
                value += candidate.IsTrump ? 22 : -8;
            }

            // 记牌：这一手领出去外面一手都压不住，那就是白拿的，加分。
            // 困难档还会用空门推断判断对头有没有主牌——都没主了，这门就随便领。
            // 同样用"预计"而不是原始张数：锅底里的牌压不住我。
            var countTrumps = model is null || !OpponentsOutOfTrumps(context, model);
            if (memory.ExpectedBeating(candidate, candidate, trump, countTrumps) == 0)
            {
                value += 15;
            }

            return value;
        });
    }
}
