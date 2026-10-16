using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Trump;
using FanKou.Protocol;
using FanKou.Session;

namespace FanKou.Web.Services;

/// <summary>桌上某一手已经打出来的牌。</summary>
public sealed record PlayView(Seat Seat, IReadOnlyList<Card> Cards, string Text);

/// <summary>
/// 牌桌要显示的东西。单人模式从 <see cref="TableSnapshot"/> 来，联机从服务端的
/// <see cref="StateDto"/> 来——两条路最后都汇到这一个视图模型，
/// 所以两边的牌桌长得一模一样，不用维护两份画法。
/// </summary>
public sealed record TableView
{
    public IReadOnlyList<PlayView> Trick { get; init; } = [];

    public Seat HumanSeat { get; init; } = Seat.South;

    public string TrumpText { get; init; } = "还没定主";

    public string LevelText { get; init; } = "-";

    public Seat? DeclarerSeat { get; init; }

    public Team DeclarerTeam { get; init; }

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public IReadOnlyList<int> Remaining { get; init; } = [0, 0, 0, 0];

    public int TrickNumber { get; init; }

    public IReadOnlyList<Card> ShownCards { get; init; } = [];

    public Seat? ShownCardsSeat { get; init; }

    /// <summary>[东西, 南北] 各打几。</summary>
    public IReadOnlyList<string> TeamLevelText { get; init; } = ["-", "-"];

    public static TableView Empty { get; } = new();

    public static TableView From(TableSnapshot snapshot, GameEngine engine) => new()
    {
        Trick = [.. snapshot.Trick.Select(p => new PlayView(p.Seat, p.Combo.Cards, p.Combo.Describe()))],
        HumanSeat = snapshot.HumanSeat,
        TrumpText = snapshot.TrumpText,
        LevelText = snapshot.LevelText,
        DeclarerSeat = snapshot.DeclarerSeat,
        DeclarerTeam = snapshot.DeclarerTeam,
        DefenderPoints = snapshot.DefenderPoints,
        DeclarerPoints = snapshot.DeclarerPoints,
        Remaining = snapshot.RemainingCounts,
        TrickNumber = snapshot.TrickNumber,
        ShownCards = snapshot.ShownCards,
        ShownCardsSeat = snapshot.ShownCardsSeat,
        TeamLevelText =
        [
            engine.RankOf(Team.EastWest).Text(),
            engine.RankOf(Team.NorthSouth).Text(),
        ],
    };

    /// <param name="humanSeat">
    /// 你自己坐哪个座位。牌桌靠它把"自己"转到屏幕下方——
    /// 换座位之后你**仍然在下面**，转的是东南西北的方位；
    /// 这里要是写死成南，坐到东家的人就会看到自己那一手横在右边。
    /// </param>
    public static TableView From(StateDto state, Seat humanSeat) => new()
    {
        Trick = [.. state.Trick.Select(p => new PlayView((Seat)p.Seat, Wire.Cards(p.Cards), p.Text))],
        HumanSeat = humanSeat,
        TrumpText = state.TrumpText,
        LevelText = state.LevelText,
        DeclarerSeat = state.DeclarerSeat >= 0 ? (Seat)state.DeclarerSeat : null,
        DeclarerTeam = (Team)state.DeclarerTeam,
        DefenderPoints = state.DefenderPoints,
        DeclarerPoints = state.DeclarerPoints,
        Remaining = state.Remaining,
        TrickNumber = state.TrickNumber,
        ShownCards = Wire.Cards(state.ShownCards),
        ShownCardsSeat = state.ShownCardsSeat >= 0 ? (Seat)state.ShownCardsSeat : null,
        TeamLevelText = state.TeamLevelText.Length == 2 ? state.TeamLevelText : ["-", "-"],
    };
}
