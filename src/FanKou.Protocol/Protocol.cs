using System.Text.Json;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Session;

namespace FanKou.Protocol;

/// <summary>
/// 联机协议。两边都是自己人，所以没有版本协商那套，改字段就一起改。
///
/// **只传牌的 Id（0..161 那个整数）**，不传牌对象——两边都有 Core，`Card.FromId` 能还原。
/// 手牌是每个座位单独发自己的，服务端绝不会把别人的手牌塞进消息里
/// （锅底同理，没翻开之前一个字节都不发）。
/// </summary>
public sealed record ClientMessage
{
    /// <summary>create / join / addBot / removeBot / start / next / call / bottom / play / leave / ping</summary>
    public string T { get; init; } = string.Empty;

    public string? Code { get; init; }

    public string? Name { get; init; }

    /// <summary>叫主/反主选的选项下标；-1 = 不叫（让队友 / 不反）。</summary>
    public int? Option { get; init; }

    public int[]? Cards { get; init; }

    /// <summary>join 时用它：true = 我要观战，不坐牌桌。</summary>
    public bool? Watch { get; init; }

    /// <summary>换座位/加人机/删人机时的目标：0..3 是牌桌座位。</summary>
    public int? Seat { get; init; }

    /// <summary>换到哪个观战位（0..2）。</summary>
    public int? WatchIndex { get; init; }
}

public sealed record ServerMessage
{
    /// <summary>joined / room / state / result / error / pong / left</summary>
    public string T { get; init; } = string.Empty;

    public string? Code { get; init; }

    public int? Seat { get; init; }

    public bool? Host { get; init; }

    public SeatInfo[]? Seats { get; init; }

    /// <summary>三个观战位。</summary>
    public WatcherInfo[]? Watchers { get; init; }

    /// <summary>自己坐的是哪个观战位；不是观战就是 -1。</summary>
    public int? WatchIndex { get; init; }

    public bool? Started { get; init; }

    public StateDto? State { get; init; }

    public RoundDto? Result { get; init; }

    public string? Message { get; init; }
}

/// <summary>座位席位上显示的：这个位子是谁、在不在线。</summary>
public sealed record SeatInfo(int Seat, string Kind, string Name, bool Connected)
{
    /// <summary>ai / human / empty</summary>
    public static string KindOf(SeatKind kind, bool occupied) => !occupied ? "empty" : kind == SeatKind.Ai ? "ai" : "human";
}

/// <summary>一个观战位。</summary>
public sealed record WatcherInfo(int Index, string Name, bool Connected);

public sealed record PlayDto(int Seat, int[] Cards, string Text);

public sealed record ComboDto(int[] Cards, string Text);

public sealed record CallOptionDto(int Index, string Text, string Spec, int[] ShowCards);

public sealed record PendingDto
{
    /// <summary>call / bottom / play</summary>
    public string Kind { get; init; } = string.Empty;

    public bool IsCounter { get; init; }

    public bool CanPass { get; init; }

    public CallOptionDto[] Options { get; init; } = [];

    public int[] Lead { get; init; } = [];

    public string? LeadText { get; init; }

    public ComboDto[] Candidates { get; init; } = [];

    /// <summary>换底牌：手上 25 + 锅底 8，共 33 张（这是该他挑的范围，全给他）。</summary>
    public int[] Pool { get; init; } = [];

    public int[] Bottom { get; init; } = [];

    public int[] Locked { get; init; } = [];
}

public sealed record StateDto
{
    public int Seq { get; init; }

    public int RoundNumber { get; init; }

    public string TrumpText { get; init; } = "还没定主";

    public string LevelText { get; init; } = "-";

    public int DeclarerSeat { get; init; } = -1;

    public int DeclarerTeam { get; init; }

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public int[] Remaining { get; init; } = [];

    /// <summary>[东西, 南北] 各打几。客户端没有引擎，服务端顺手算好给它。</summary>
    public string[] TeamLevelText { get; init; } = [];

    public int TrickNumber { get; init; }

    public int[] Bottom { get; init; } = [];

    public bool BottomRevealed { get; init; }

    public int[] ShownCards { get; init; } = [];

    public int ShownCardsSeat { get; init; } = -1;

    public string Status { get; init; } = string.Empty;

    public PlayDto[] Trick { get; init; } = [];

    /// <summary>只给收信人自己那 25 张。</summary>
    public int[] Hand { get; init; } = [];

    public PendingDto? Pending { get; init; }

    public int SecondsLeft { get; init; }

    public bool AutoPlay { get; init; }

    public bool CanUndo { get; init; }

    public string[] Log { get; init; } = [];
}

public sealed record RoundDto(
    int RoundNumber,
    string OutcomeText,
    int DefenderPoints,
    int DeclarerPoints,
    int[] Bottom,
    string BottomText,
    string LevelBefore,
    string LevelAfter,
    string NextRoundText,
    bool RoomOver);

/// <summary>序列化和"牌 ←→ 整数"的编解码。类名故意不叫 Protocol，免得和命名空间撞。</summary>
public static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static int[] Ids(IEnumerable<Card> cards) => [.. cards.Select(c => c.Id)];

    public static Card[] Cards(IEnumerable<int> ids) => [.. ids.Select(Card.FromId)];

    public static ComboDto Combo(CardCombination combo) => new(Ids(combo.Cards), combo.Describe());
}