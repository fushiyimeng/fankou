using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Trump;
using FanKou.Protocol;
using FanKou.Session;

namespace FanKou.Server;

/// <summary>一条客户端连接。它要么坐在牌桌上（<see cref="Seat"/>），要么在观战位（<see cref="WatcherIndex"/>）。</summary>
public sealed class Connection(WebSocket socket)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocket Socket { get; } = socket;

    public Room? Room { get; set; }

    public Seat Seat { get; set; }

    /// <summary>-1 = 不是观战；0..2 = 在第几个观战位。</summary>
    public int WatcherIndex { get; set; } = -1;

    public bool IsWatcher => WatcherIndex >= 0;

    public string Name { get; set; } = "玩家";

    public async Task SendAsync(ServerMessage message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Wire.Json);

        await _sendLock.WaitAsync();
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // 对端已经跑了，下面的收尾会处理。
        }
        finally
        {
            _sendLock.Release();
        }
    }
}

/// <summary>
/// 一个房间。房号是 4 位数字，同时存在的房间之间唯一。
///
/// 里面有 **4 个牌桌座位**（南 北 东 西）和 **3 个观战位**。
/// 座位按加入顺序分：南 → 北 → 东 → 西；前两个是对家，所以两个人玩刚好一队。
/// 等开局的时候可以随便换座位、也可以把人机塞到指定的空位。
/// </summary>
public sealed class Room(string code, bool fast = false)
{
    /// <summary>加入顺序。南和北是对家，先来的两个人自动成一队。</summary>
    public static readonly Seat[] JoinOrder = [Seat.South, Seat.North, Seat.East, Seat.West];

    public const int WatcherSeats = 3;

    private readonly ConcurrentDictionary<Seat, Connection> _connections = new();
    private readonly Connection?[] _watchers = new Connection?[WatcherSeats];
    private readonly string?[] _watcherNames = new string?[WatcherSeats];
    private readonly string?[] _names = new string?[4];
    private readonly bool[] _bots = new bool[4];
    private readonly SemaphoreSlim _pump = new(1, 1);

    private Connection? _host;
    private int _dirty;
    private int _sequence;

    public string Code { get; } = code;

    public GameSession? Session { get; private set; }

    public bool Started => Session is not null;

    public bool Host(Connection connection) => ReferenceEquals(_host, connection);

    public IEnumerable<Connection> Everyone() =>
        _connections.Values.Concat(_watchers.Where(w => w is not null).Cast<Connection>());

    public bool IsEmpty => _connections.IsEmpty && _watchers.All(w => w is null);

    // ------------------------------------------------------------ 座位

    private bool Occupied(Seat seat) => _names[(int)seat] is not null || _bots[(int)seat];

    private int FreeWatcherSeat()
    {
        for (var i = 0; i < WatcherSeats; i++)
        {
            if (_watchers[i] is null && _watcherNames[i] is null)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>按加入顺序（南 北 东 西）列，不是按枚举顺序（东 南 西 北）——后者看着别扭。</summary>
    public SeatInfo[] SeatInfos() =>
    [
        .. JoinOrder.Select(seat =>
        {
            var human = _names[(int)seat] is not null;
            var kind = _bots[(int)seat] ? SeatKind.Ai : human ? SeatKind.RemoteHuman : SeatKind.Ai;

            return new SeatInfo(
                (int)seat,
                SeatInfo.KindOf(kind, Occupied(seat)),
                _names[(int)seat] ?? (_bots[(int)seat] ? $"电脑{seat.Name()}" : "空位"),
                _connections.ContainsKey(seat));
        }),
    ];

    public WatcherInfo[] WatcherInfos() =>
    [
        .. Enumerable.Range(0, WatcherSeats).Select(i => new WatcherInfo(
            i,
            _watcherNames[i] ?? "空着",
            _watchers[i] is not null)),
    ];

    public bool CanStart => Seats.All.All(Occupied) && Seats.All.Any(seat => _names[(int)seat] is not null);

    // ------------------------------------------------------------ 进出

    public bool TryJoin(Connection connection, string name, bool watch, out string? error)
    {
        error = null;

        if (Started)
        {
            // 已经开局：先看有没有"人掉了、座位还留着"的位子可以顶回去。
            if (!watch)
            {
                var orphan = JoinOrder.FirstOrDefault(s => _names[(int)s] is not null && !_connections.ContainsKey(s));

                if (_names[(int)orphan] is not null)
                {
                    TakeSeat(connection, orphan, name);
                    Session?.SetAutoPlay(orphan, false);
                    return true;
                }
            }

            var slot = FreeWatcherSeat();
            if (slot < 0)
            {
                error = "房间满了";
                return false;
            }

            SetWatcher(connection, slot, name);
            return true;
        }

        if (!watch)
        {
            var free = JoinOrder.FirstOrDefault(s => !Occupied(s));

            if (!Occupied(free))
            {
                TakeSeat(connection, free, name);
                return true;
            }
        }

        var index = FreeWatcherSeat();
        if (index >= 0)
        {
            SetWatcher(connection, index, name);
            return true;
        }

        error = "房间满了";
        return false;
    }

    private void TakeSeat(Connection connection, Seat seat, string name)
    {
        _names[(int)seat] = name;
        _connections[seat] = connection;
        connection.Room = this;
        connection.Seat = seat;
        connection.WatcherIndex = -1;
        connection.Name = name;
        _host ??= connection;
    }

    private void SetWatcher(Connection connection, int index, string name)
    {
        _watchers[index] = connection;
        _watcherNames[index] = name;
        connection.Room = this;
        connection.WatcherIndex = index;
        connection.Name = name;
    }

    /// <summary>换座位。目标有人就跟他换，目标是电脑就顶掉电脑，空着就搬过去。</summary>
    public bool TrySwitch(Connection connection, Seat target, out string? error)
    {
        error = null;

        if (Started)
        {
            error = "开局之后不能换座位了";
            return false;
        }

        if (connection.IsWatcher)
        {
            if (Occupied(target))
            {
                error = "那个位子有人";
                return false;
            }

            _watchers[connection.WatcherIndex] = null;
            _watcherNames[connection.WatcherIndex] = null;
            connection.WatcherIndex = -1;
            TakeSeat(connection, target, connection.Name);
            return true;
        }

        if (connection.Seat == target)
        {
            return true;
        }

        var mine = connection.Seat;

        if (_bots[(int)target])
        {
            // 顶掉人机
            _bots[(int)target] = false;
            _connections.TryRemove(mine, out _);
            _names[(int)mine] = null;
            _names[(int)target] = connection.Name;
            _connections[target] = connection;
            connection.Seat = target;
            return true;
        }

        if (_connections.TryGetValue(target, out var other))
        {
            // 两个人对调
            _connections[target] = connection;
            _connections[mine] = other;
            connection.Seat = target;
            other.Seat = mine;
            (_names[(int)mine], _names[(int)target]) = (_names[(int)target], _names[(int)mine]);
            return true;
        }

        // 空位
        _connections.TryRemove(mine, out _);
        _names[(int)mine] = null;
        _names[(int)target] = connection.Name;
        _connections[target] = connection;
        connection.Seat = target;
        return true;
    }

    /// <summary>下桌去观战。</summary>
    public bool TryWatch(Connection connection, int index, out string? error)
    {
        error = null;

        if (Started)
        {
            error = "开局之后不能换位了";
            return false;
        }

        if (index < 0 || index >= WatcherSeats)
        {
            error = "没有这个观战位";
            return false;
        }

        if (_watchers[index] is not null || _watcherNames[index] is not null)
        {
            error = "这个观战位有人";
            return false;
        }

        if (connection.IsWatcher)
        {
            _watchers[connection.WatcherIndex] = null;
            _watcherNames[connection.WatcherIndex] = null;
        }
        else
        {
            _connections.TryRemove(connection.Seat, out _);
            _names[(int)connection.Seat] = null;
        }

        SetWatcher(connection, index, connection.Name);
        return true;
    }

    public void Leave(Connection connection)
    {
        if (connection.IsWatcher)
        {
            var index = connection.WatcherIndex;
            if (index >= 0 && index < WatcherSeats && ReferenceEquals(_watchers[index], connection))
            {
                _watchers[index] = null;
                _watcherNames[index] = null;
            }

            connection.WatcherIndex = -1;
        }
        else if (_connections.TryRemove(connection.Seat, out _))
        {
            if (Started)
            {
                // 人跑了但座位留着：先托管，别把整局卡死。他回来还能顶上。
                Session?.SetAutoPlay(connection.Seat, true);
            }
            else
            {
                _names[(int)connection.Seat] = null;
            }
        }

        if (ReferenceEquals(_host, connection))
        {
            _host = _connections.Values.FirstOrDefault() ?? _watchers.FirstOrDefault(w => w is not null);
        }

        BroadcastRoom();
    }

    // ------------------------------------------------------------ 人机

    public bool AddBot(Seat seat, out string? error)
    {
        error = null;

        if (Started)
        {
            error = "已经开局了";
            return false;
        }

        if (_bots[(int)seat])
        {
            error = "这个位子已经有人机了";
            return false;
        }

        if (_names[(int)seat] is not null)
        {
            error = "这个位子有人";
            return false;
        }

        _bots[(int)seat] = true;
        return true;
    }

    public bool RemoveBot(Seat seat, out string? error)
    {
        error = null;

        if (Started)
        {
            error = "已经开局了";
            return false;
        }

        if (!_bots[(int)seat])
        {
            error = "这个位子没有人机";
            return false;
        }

        _bots[(int)seat] = false;
        return true;
    }

    // ------------------------------------------------------------ 开局

    public bool Start(out string? error)
    {
        error = null;

        if (Started)
        {
            error = "已经开局了";
            return false;
        }

        if (!CanStart)
        {
            error = "四个位子要凑满才能开始（人不够就给空位加人机）";
            return false;
        }

        StartMatch();
        return true;
    }

    private Dictionary<Seat, SeatKind> Kinds() => Seats.All.ToDictionary(
        seat => seat,
        seat => _bots[(int)seat] ? SeatKind.Ai : SeatKind.RemoteHuman);

    private void StartMatch()
    {
        Session?.Dispose();

        var session = GameSession.CreateForRoom(Kinds());

        if (fast)
        {
            // 自检用：把节拍压到最小，不然一局要一分半。
            session.AiThinkDelayMs = 1;
            session.TrickPauseMs = 1;
            session.DealRoundMs = 1;
            session.HumanTimeoutSeconds = 5;
            session.CallTimeoutSeconds = 5;
        }

        session.Changed += OnSessionChanged;
        session.CountdownChanged += OnSessionChanged;
        session.RoundFinished += OnRoundFinished;
        Session = session;

        StartSlowTick();
        session.Start();
        BroadcastRoom();
        BroadcastState();
    }

    /// <summary>整盘打完就重开一盘，座位不变。</summary>
    public void RestartMatch() => StartMatch();

    public void NextRound()
    {
        if (Session is { } session && session.Engine.Phase == GamePhase.RoundOver)
        {
            session.StartNextRound();
        }
    }

    // ------------------------------------------------------------ 收/发

    private void OnSessionChanged() => BroadcastState();

    private void OnRoundFinished(RoundSummary summary)
    {
        _ = Task.Run(async () =>
        {
            foreach (var connection in Everyone())
            {
                await connection.SendAsync(new ServerMessage { T = "result", Code = Code, Result = ToDto(summary) });
            }

            // 给玩家看一眼结算，然后自己往下走；有人点了「下一局」就不用等了。
            await Task.Delay(summary.RoomOver ? 10_000 : 8_000);

            if (Session is not { } session || session.Engine.Phase != GamePhase.RoundOver)
            {
                return;
            }

            if (summary.RoomOver)
            {
                RestartMatch();
            }
            else
            {
                session.StartNextRound();
            }
        });
    }

    private static RoundDto ToDto(RoundSummary s) => new(
        s.RoundNumber,
        s.OutcomeText,
        s.DefenderPoints,
        s.DeclarerPoints,
        Wire.Ids(s.Bottom),
        s.BottomText,
        s.LevelBefore,
        s.LevelAfter,
        s.NextRoundText,
        s.RoomOver);

    /// <summary>读秒每秒跳一次，但状态不用每秒重发——只有有人在读秒时才补推。</summary>
    private void StartSlowTick()
    {
        _ = Task.Run(async () =>
        {
            while (Session is not null)
            {
                await Task.Delay(1000);

                if (_connections.Values.Any(c => Session?.SecondsLeftFor(c.Seat) > 0))
                {
                    BroadcastState();
                }
            }
        });
    }

    public void BroadcastRoom()
    {
        var seats = SeatInfos();
        var watchers = WatcherInfos();
        var started = Started;

        foreach (var connection in Everyone())
        {
            _ = connection.SendAsync(new ServerMessage
            {
                T = "room",
                Code = Code,
                Seat = connection.IsWatcher ? -1 : (int)connection.Seat,
                WatchIndex = connection.WatcherIndex,
                Host = Host(connection),
                Seats = seats,
                Watchers = watchers,
                Started = started,
            });
        }
    }

    /// <summary>合并高频刷新：上一个还没发完就只记个"脏"，发完再补一次。</summary>
    public void BroadcastState()
    {
        Interlocked.Exchange(ref _dirty, 1);

        if (_pump.Wait(0))
        {
            _ = PumpAsync();
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _dirty, 0) == 1)
            {
                if (Session is not null)
                {
                    Interlocked.Increment(ref _sequence);

                    foreach (var connection in Everyone())
                    {
                        await connection.SendAsync(new ServerMessage
                        {
                            T = "state",
                            Code = Code,
                            Seat = connection.IsWatcher ? -1 : (int)connection.Seat,
                            WatchIndex = connection.WatcherIndex,
                            State = BuildState(connection),
                        });
                    }
                }
            }
        }
        finally
        {
            _pump.Release();

            if (Volatile.Read(ref _dirty) == 1 && _pump.Wait(0))
            {
                _ = PumpAsync();
            }
        }
    }

    private StateDto BuildState(Connection connection)
    {
        var session = Session!;
        var snapshot = session.Snapshot();

        // 观战的人**什么手牌都看不到**，也没有待决策——这是观战位和座位的唯一区别。
        var watching = connection.IsWatcher;
        var seat = connection.Seat;

        return new StateDto
        {
            Seq = _sequence,
            RoundNumber = session.Engine.RoundNumber,
            TrumpText = snapshot.TrumpText,
            LevelText = snapshot.LevelText,
            DeclarerSeat = snapshot.DeclarerSeat is { } declarer ? (int)declarer : -1,
            DeclarerTeam = (int)snapshot.DeclarerTeam,
            DefenderPoints = snapshot.DefenderPoints,
            DeclarerPoints = snapshot.DeclarerPoints,
            Remaining = [.. snapshot.RemainingCounts],
            TeamLevelText =
            [
                session.Engine.RankOf(Team.EastWest).Text(),
                session.Engine.RankOf(Team.NorthSouth).Text(),
            ],
            TrickNumber = snapshot.TrickNumber,

            // 锅底没翻开之前一个字节都不发，翻了才发。
            Bottom = snapshot.BottomRevealed ? Wire.Ids(snapshot.Bottom) : [],
            BottomRevealed = snapshot.BottomRevealed,
            ShownCards = Wire.Ids(snapshot.ShownCards),
            ShownCardsSeat = snapshot.ShownCardsSeat is { } shown ? (int)shown : -1,
            Status = snapshot.Status,

            Trick =
            [
                .. snapshot.Trick.Select(p => new PlayDto((int)p.Seat, Wire.Ids(p.Combo.Cards), p.Combo.Describe())),
            ],

            // 客户端没有引擎，排不了"主牌在前"，所以在这儿排好再发。
            Hand = watching ? [] : Wire.Ids(CardSorter.ForDisplay(session.HandOf(seat), session.Engine.Trump)),
            Pending = watching ? null : BuildPending(seat),
            SecondsLeft = watching ? 0 : session.SecondsLeftFor(seat),
            AutoPlay = !watching && session.IsAutoPlay(seat),
            CanUndo = !watching && seat == session.HumanSeat && session.CanUndo,
            Log = [.. session.Log.TakeLast(40)],
        };
    }

    private PendingDto? BuildPending(Seat seat)
    {
        var session = Session!;

        switch (session.PendingFor(seat))
        {
            case PendingDecision.NeedCall call:
                return new PendingDto
                {
                    Kind = "call",
                    IsCounter = call.IsCounter,
                    CanPass = call.CanPass,
                    Options =
                    [
                        .. call.Options.Select((option, index) => new CallOptionDto(
                            index,
                            option.Describe(),
                            SpecText(option.Kind),
                            Wire.Ids(option.ShowCards))),
                    ],
                };

            case PendingDecision.NeedBottomSwap swap:
                return new PendingDto
                {
                    Kind = "bottom",
                    Pool = Wire.Ids(CardSorter.ForDisplay(swap.Pool, session.Engine.Trump)),
                    Bottom = Wire.Ids(swap.Bottom),
                    Locked = Wire.Ids(swap.Locked),
                };

            case PendingDecision.NeedPlay play:
                return new PendingDto
                {
                    Kind = "play",
                    Lead = play.Lead is null ? [] : Wire.Ids(play.Lead.Cards),
                    LeadText = play.Lead?.Describe(),
                    Candidates = [.. play.Candidates.Select(Wire.Combo)],
                };

            default:
                return null;
        }
    }

    private static string SpecText(CallKind kind) => kind switch
    {
        CallKind.SuitedSuit => "规格：叫主（最低）",
        CallKind.SuitedBoth => "规格：定主（比叫主大）",
        CallKind.SmallNoTrump => "规格：小无主",
        CallKind.BigNoTrump => "规格：大无主（最高）",
        _ => string.Empty,
    };

    // ------------------------------------------------------------ 动作

    /// <summary>
    /// 执行一步动作。**座位取自连接**，不看消息里写的什么——
    /// 不然谁都能替别人出牌；观战的人一步也不能走。
    /// </summary>
    public void Apply(Connection connection, ClientMessage message)
    {
        if (Session is not { } session || connection.IsWatcher)
        {
            return;
        }

        var seat = connection.Seat;

        switch (message.T)
        {
            case "call" when session.PendingFor(seat) is PendingDecision.NeedCall call:
            {
                var index = message.Option ?? -1;
                var option = index >= 0 && index < call.Options.Count ? call.Options[index] : null;
                session.SubmitCall(seat, option);
                break;
            }

            case "bottom":
                if (message.Cards is { Length: 8 } cards)
                {
                    session.SubmitBottomDiscards(seat, Wire.Cards(cards));
                }

                break;

            case "play":
                if (message.Cards is { Length: > 0 } play)
                {
                    session.SubmitPlay(seat, Wire.Cards(play));
                }

                break;
        }
    }
}

/// <summary>所有房间的总入口：发房号、接连接、把消息转给对应的房间。</summary>
public sealed class RoomServer(bool fast = false)
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly Random _random = new();

    public int RoomCount => _rooms.Count;

    public Room? Find(string? code) =>
        code is not null && _rooms.TryGetValue(code, out var room) ? room : null;

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellation)
    {
        var connection = new Connection(socket);
        var buffer = new byte[16 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                var text = await ReceiveAsync(socket, buffer, cancellation);
                if (text is null)
                {
                    break;
                }

                ClientMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<ClientMessage>(text, Wire.Json);
                }
                catch (JsonException)
                {
                    await connection.SendAsync(new ServerMessage { T = "error", Message = "看不懂的消息" });
                    continue;
                }

                if (message is not null)
                {
                    await DispatchAsync(connection, message);
                }
            }
        }
        catch (Exception)
        {
            // 掉线而已。
        }
        finally
        {
            connection.Room?.Leave(connection);
            Cleanup(connection.Room);
        }
    }

    private async Task DispatchAsync(Connection connection, ClientMessage message)
    {
        switch (message.T)
        {
            case "ping":
                await connection.SendAsync(new ServerMessage { T = "pong" });
                return;

            case "create":
            {
                var room = CreateRoom();
                Join(room, connection, message.Name, message.Watch ?? false);
                break;
            }

            case "join":
            {
                var room = Find(message.Code);
                if (room is null)
                {
                    await connection.SendAsync(new ServerMessage { T = "error", Message = "没有这个房号" });
                    return;
                }

                Join(room, connection, message.Name, message.Watch ?? false);
                break;
            }

            case "addBot":
            case "removeBot":
            case "start":
            case "switch":
            case "watch":
            {
                if (connection.Room is not { } room)
                {
                    return;
                }

                // 换座位/去观战是自己就能定的；动房间配置要房主。
                var hostOnly = message.T is "addBot" or "removeBot" or "start";

                if (hostOnly && !room.Host(connection))
                {
                    await connection.SendAsync(new ServerMessage { T = "error", Message = "只有房主能改房间" });
                    return;
                }

                string? error = null;

                switch (message.T)
                {
                    case "addBot":
                        if (message.Seat is { } addSeat && addSeat is >= 0 and <= 3)
                        {
                            room.AddBot((Seat)addSeat, out error);
                        }

                        break;

                    case "removeBot":
                        if (message.Seat is { } removeSeat && removeSeat is >= 0 and <= 3)
                        {
                            room.RemoveBot((Seat)removeSeat, out error);
                        }

                        break;

                    case "start":
                        room.Start(out error);
                        break;

                    case "switch":
                        if (message.Seat is { } target && target is >= 0 and <= 3)
                        {
                            room.TrySwitch(connection, (Seat)target, out error);
                        }

                        break;

                    case "watch":
                        room.TryWatch(connection, message.WatchIndex ?? -1, out error);
                        break;
                }

                if (error is not null)
                {
                    await connection.SendAsync(new ServerMessage { T = "error", Message = error });
                }
                else
                {
                    await AfterRoomChange(connection, room);
                }

                break;
            }

            case "next":
                connection.Room?.NextRound();
                break;

            case "call":
            case "bottom":
            case "play":
                connection.Room?.Apply(connection, message);
                break;

            case "leave":
                connection.Room?.Leave(connection);
                connection.Room = null;
                break;
        }
    }

    private static async Task AfterRoomChange(Connection connection, Room room)
    {
        await connection.SendAsync(new ServerMessage
        {
            T = "joined",
            Code = room.Code,
            Seat = connection.IsWatcher ? -1 : (int)connection.Seat,
            WatchIndex = connection.WatcherIndex,
            Host = room.Host(connection),
            Seats = room.SeatInfos(),
            Watchers = room.WatcherInfos(),
            Started = room.Started,
        });

        room.BroadcastRoom();
        room.BroadcastState();
    }

    private void Join(Room room, Connection connection, string? name, bool watch)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? "玩家" : name.Trim();

        if (!room.TryJoin(connection, clean, watch, out var error))
        {
            _ = connection.SendAsync(new ServerMessage { T = "error", Message = error ?? "进不去" });
            return;
        }

        _ = AfterRoomChange(connection, room);
    }

    private Room CreateRoom()
    {
        lock (_random)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var code = _random.Next(1000, 10000).ToString();
                var room = new Room(code, fast);

                if (_rooms.TryAdd(code, room))
                {
                    return room;
                }
            }
        }

        throw new InvalidOperationException("房号用光了");
    }

    private void Cleanup(Room? room)
    {
        if (room is not null && room.IsEmpty && !room.Started)
        {
            _rooms.TryRemove(room.Code, out _);
        }
    }

    private static async Task<string?> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken cancellation)
    {
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellation);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }

            if (stream.Length > 512 * 1024)
            {
                return null;
            }
        }
    }
}
