using FanKou.Core.Cards;
using FanKou.Core.Game;

namespace FanKou.Session;

/// <summary>
/// 牌桌要显示的东西。会话在状态变化后填一份给界面刷新。
///
/// 这份和桌面版 <c>FanKou.Ui.Controls.TableSnapshot</c> 是同构的——
/// 桌面版那个定义在 WinForms 项目里，网页版（browser-wasm）引不到，
/// 所以在这一层再声明一份。两边都只是"视图模型"，不含任何规则，
/// 改动时只要别让字段含义漂移就行。
/// </summary>
public sealed record TableSnapshot
{
    public IReadOnlyList<TrickPlay> Trick { get; init; } = [];

    public Seat HumanSeat { get; init; } = Seat.South;

    public string TrumpText { get; init; } = "还没定主";

    public string LevelText { get; init; } = "-";

    public Team DeclarerTeam { get; init; } = Team.EastWest;

    public Seat? DeclarerSeat { get; init; }

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public IReadOnlyList<int> RemainingCounts { get; init; } = [0, 0, 0, 0];

    public int TrickNumber { get; init; }

    public IReadOnlyList<Card> Bottom { get; init; } = [];

    public bool BottomRevealed { get; init; }

    /// <summary>叫主时摆出来给大家看的明牌，开打之前一直摊在桌上。</summary>
    public IReadOnlyList<Card> ShownCards { get; init; } = [];

    public Seat? ShownCardsSeat { get; init; }

    public string Status { get; init; } = string.Empty;

    public static TableSnapshot Empty { get; } = new();
}
