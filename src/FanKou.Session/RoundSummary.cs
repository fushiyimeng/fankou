using FanKou.Core.Cards;

namespace FanKou.Session;

/// <summary>
/// 一局打完的结算信息。和桌面版 <c>FanKou.Ui.Dialogs.RoundSummary</c> 同构，
/// 理由见 <see cref="TableSnapshot"/> 的注释。
/// </summary>
public sealed record RoundSummary
{
    public int RoundNumber { get; init; }

    public string OutcomeText { get; init; } = string.Empty;

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public IReadOnlyList<Card> Bottom { get; init; } = [];

    public string BottomText { get; init; } = string.Empty;

    public string LevelBefore { get; init; } = string.Empty;

    public string LevelAfter { get; init; } = string.Empty;

    public string NextRoundText { get; init; } = string.Empty;

    public bool RoomOver { get; init; }
}
