using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;

namespace FanKou.Core.Game;

/// <summary>外部（真人或 AI）提交给引擎的动作。</summary>
public abstract record GameCommand(Seat Seat);

public sealed record DeclareCall(Seat Seat, CallOption Option) : GameCommand(Seat);

public sealed record PassCall(Seat Seat) : GameCommand(Seat);

/// <summary>换底牌：扣下 8 张，把锅底的 8 张拿上来。</summary>
public sealed record CommitBottom(Seat Seat, ImmutableArray<Card> Discards) : GameCommand(Seat);

public sealed record PlayCards(Seat Seat, ImmutableArray<Card> Cards) : GameCommand(Seat);
