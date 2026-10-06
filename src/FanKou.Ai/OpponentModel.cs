using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Play;
using FanKou.Core.Trump;

namespace FanKou.Ai;

/// <summary>
/// 根据已经打出来的牌推算别家手上大概是什么情况。
///
/// 目前只用一条最硬的信息：**谁空门了**。
/// 规则上跟牌时"有该花色必须优先给该花色"，手上的不够就得全给光。
/// 所以某家出的牌里属于领出组的张数**少于领出的张数**，就说明他从这一轮起这门彻底空了——
/// 这是白纸黑字能推出来的，不是猜的。
/// </summary>
public sealed class OpponentModel
{
    private readonly Dictionary<Seat, HashSet<FollowGroup>> _voids = [];

    private OpponentModel()
    {
    }

    public static OpponentModel Build(PublicBoardInfo info, TrumpContext trump)
    {
        var model = new OpponentModel();

        foreach (var trick in info.CompletedTricks)
        {
            model.Record(trick.Plays, trump);
        }

        model.Record(info.CurrentTrick, trump);
        return model;
    }

    /// <summary>这家是不是这门已经空了。</summary>
    public bool IsVoid(Seat seat, FollowGroup group) =>
        _voids.TryGetValue(seat, out var set) && set.Contains(group);

    public IReadOnlyCollection<FollowGroup> VoidsOf(Seat seat) =>
        _voids.TryGetValue(seat, out var set) ? set : [];

    /// <summary>四家里有几家在这门上已经空了。</summary>
    public int VoidCount(FollowGroup group) =>
        Seats.All.Count(seat => IsVoid(seat, group));

    /// <summary>
    /// 这家有没有可能用主牌杀这一门（空门了这门，而且还没表现出主牌空了）。
    /// 只能说"有可能"——他手上到底有没有主牌是看不到的。
    /// </summary>
    public bool MightRuff(Seat seat, FollowGroup group) =>
        group != FollowGroup.Trump
        && IsVoid(seat, group)
        && !IsVoid(seat, FollowGroup.Trump);

    private void Record(IReadOnlyList<TrickPlay> plays, TrumpContext trump)
    {
        if (plays.Count < 2)
        {
            return;
        }

        var lead = plays[0].Combo;
        var group = FollowContext.LeadGroupOf(lead);
        var needed = lead.Length;

        for (var i = 1; i < plays.Count; i++)
        {
            var play = plays[i];
            var given = play.Combo.Cards.Count(c => IdentityResolver.Resolve(c, trump).Group == group);

            if (given < needed)
            {
                // 该给这么多却没给够 —— 他这门是真的没牌了。
                if (!_voids.TryGetValue(play.Seat, out var set))
                {
                    set = [];
                    _voids[play.Seat] = set;
                }

                set.Add(group);
            }
        }
    }
}
