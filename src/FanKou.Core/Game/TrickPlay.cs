using FanKou.Core.Combinations;

namespace FanKou.Core.Game;

/// <summary>一墩牌里某一家出的牌。</summary>
public sealed record TrickPlay(Seat Seat, CardCombination Combo);
