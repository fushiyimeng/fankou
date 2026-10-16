using System.Collections.Immutable;
using FanKou.Ai;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Levels;
using FanKou.Core.Play;
using FanKou.Core.Scoring;
using FanKou.Core.Util;

namespace FanKou.Session;

/// <summary>
/// 推进对局：轮到真人就停下等他操作，轮到 AI 就隔一小会儿自己出牌。
/// 界面只读它给的状态快照，不自己维护游戏状态——这是这类游戏最常见的一类 bug。
///
/// 这是桌面版 <c>FanKou.App.GameController</c> 的无界面版本，三处不同：
/// <list type="number">
/// <item>WinForms 的 <c>Timer</c> 换成 async 等待（可注入的节拍），浏览器里也能跑；</item>
/// <item>"人类座位"换成一个可分配的 <see cref="SeatKind"/>，为以后联机留口子；</item>
/// <item><c>Dispose</c> 换成取消令牌，不再依赖窗体生命周期。</item>
/// </list>
/// 规则、节拍、托管、撤回、超时兜底的语义都跟桌面版保持一致，
/// 别在这里另起一套，否则两个版本的行为会慢慢跑偏。
/// </summary>
public sealed class GameSession : IDisposable
{
    private const int CardsPerSeat = 25;

    /// <summary>唤醒信号。循环在等节拍/等人时被它打断，重新算下一步该干什么。</summary>
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// 联机时"提交动作"是从 WebSocket 线程进来的，而驱动循环跑在自己那条任务上。
    /// 引擎和这一层的派生状态都不是线程安全的，所以凡是读局面/改局面的地方都走这把锁。
    /// 锁只包同步代码——循环里等待节拍的部分在锁外面。
    /// </summary>
    private readonly object _gate = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _log = [];
    private readonly List<UndoPoint> _undoStack = [];
    private readonly Dictionary<Seat, SeatKind> _seatKinds = [];
    private readonly Dictionary<Seat, bool> _autoPlay = [];

    private IReadOnlyDictionary<Seat, IPlayerBrain> _brains;
    private readonly GameEngine _engine;

    private PendingDecision? _humanPending;
    private Task? _loop;
    private bool _disposed;

    /// <summary>一轮打完之后的暂停还剩多久（毫秒）。0 表示不暂停。</summary>
    private int _pauseMs;

    private int _humanSecondsLeft;
    private bool _roundFinishedFired;

    private RoundSettled? _lastSettled;
    private BottomRevealed? _lastBottom;
    private LevelsChanged? _lastLevels;

    // 刚打完那一局的庄家是谁。结算之后 _engine.DeclarerTeam 已经换成下一局的了，
    // 所以要在叫主定下来的当时记一份，不然结算框会把庄闲说反。
    private Team _roundDeclarerTeam;

    private bool _roomJustFinished;

    private IReadOnlyList<Card> _shownCards = [];
    private Seat? _shownCardsSeat;

    // 一轮打完引擎就把出牌清空了，但暂停那会儿还得让人看得见刚才出了什么。
    private ImmutableArray<TrickPlay> _lastTrickPlays = [];

    /// <summary>
    /// 一个撤回点 = 引擎快照 + 界面自己派生出来的那点状态。
    /// 后半截不能省：引擎还原了，<c>_log</c> 这些不会跟着回滚。
    /// </summary>
    private sealed record UndoPoint(
        GameEngineState Engine,
        int LogCount,
        ImmutableArray<TrickPlay> LastTrickPlays,
        IReadOnlyList<Card> ShownCards,
        Seat? ShownCardsSeat,
        Team RoundDeclarerTeam,
        bool RoomJustFinished,
        string Status);

    private enum StepKind
    {
        /// <summary>没别的事，等人操作。</summary>
        None,

        /// <summary>等一段时间再执行 <see cref="Step.Action"/>。</summary>
        Delay,

        /// <summary>真人出牌读秒，1 秒一跳，可以被操作打断。</summary>
        HumanTick,
    }

    private enum ActionKind
    {
        None,
        Pause,
        Deal,
        Ai,
    }

    private readonly record struct Step(StepKind Kind, int DelayMs, ActionKind Action)
    {
        public static readonly Step Wait = new(StepKind.None, 0, ActionKind.None);

        public static readonly Step HumanTick = new(StepKind.HumanTick, 1000, ActionKind.None);

        public static Step Pause(int ms) => new(StepKind.Delay, ms, ActionKind.Pause);

        public static Step Deal(int ms) => new(StepKind.Delay, ms, ActionKind.Deal);

        public static Step Ai(int ms) => new(StepKind.Delay, ms, ActionKind.Ai);
    }

    public GameSession(
        GameEngine engine,
        IReadOnlyDictionary<Seat, IPlayerBrain> brains,
        Seat humanSeat,
        RuleOptions options)
    {
        _engine = engine;
        _brains = brains;
        HumanSeat = humanSeat;
        Options = options;

        foreach (var seat in Seats.All)
        {
            _seatKinds[seat] = seat == humanSeat ? SeatKind.LocalHuman : SeatKind.Ai;
        }
    }

    /// <summary>电脑"思考"多久再出牌。</summary>
    public int AiThinkDelayMs { get; set; } = 620;

    /// <summary>一轮四人出完牌之后停多久，让人看清这一轮出了什么。</summary>
    public int TrickPauseMs { get; set; } = 1500;

    /// <summary>发牌时每发出一轮（四个人各一张）花多久。整副 25 轮，0.1 秒 ≈ 2.5 秒发完。</summary>
    public int DealRoundMs { get; set; } = 100;

    /// <summary>轮到真人出牌之后，多少秒不出就替他出一手。</summary>
    public int HumanTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 轮到真人叫主/反主之后，多少秒不定就替他定。
    /// 比出牌短：叫主是一锤子买卖，卡在这儿整个发牌都停着。
    /// </summary>
    public int CallTimeoutSeconds { get; set; } = 30;

    public Seat HumanSeat { get; }

    /// <summary>当前的规则开关。改了之后**下一局生效**，正在打的不受影响。</summary>
    public RuleOptions Options { get; private set; }

    public GameEngine Engine => _engine;

    public IReadOnlyList<string> Log => _log;

    public string Status { get; private set; } = "点「开始新的一局」开局";

    public PendingDecision? HumanPending => _humanPending;

    /// <summary>正在一张一张发牌。这期间不接受出牌操作。</summary>
    public bool IsDealing => _engine.Phase == GamePhase.Dealing;

    /// <summary>托管：真人座位也交给 AI 打。可以按座位单独设——联机时某个人掉线，只托管他那一份。</summary>
    public bool AutoPlayHuman => IsAutoPlay(HumanSeat);

    /// <summary>轮到你出牌的读秒；0 表示没在读秒。</summary>
    public int HumanSecondsLeft => SecondsLeftFor(HumanSeat);

    /// <summary>这个座位头上的读秒；没轮到他、或者这一步不读秒就是 0。</summary>
    public int SecondsLeftFor(Seat seat) => _humanPending?.Actor == seat ? _humanSecondsLeft : 0;

    /// <summary>这个座位是不是托管给电脑了。</summary>
    public bool IsAutoPlay(Seat seat) => _autoPlay.TryGetValue(seat, out var on) && on;

    /// <summary>轮到人出牌时，当前所有合法候选（给"提示"用）。</summary>
    public IReadOnlyList<CardCombination> HumanCandidates => CandidatesFor(HumanSeat);

    /// <summary>自己的手牌。发牌途中是发到哪张就露哪张。</summary>
    public IReadOnlyList<Card> HumanHand => _engine.HandOf(HumanSeat);

    /// <summary>这个座位是不是"人在打"（本地或远端）。联机时每台客户端的视角都不一样，所以都要能按座位取。</summary>
    public bool IsHumanSeat(Seat seat) => KindOf(seat) != SeatKind.Ai;

    /// <summary>当前等这个座位做的那一步；不是他就返回 null。</summary>
    public PendingDecision? PendingFor(Seat seat) => _humanPending?.Actor == seat ? _humanPending : null;

    public IReadOnlyList<Card> HandOf(Seat seat) => _engine.HandOf(seat);

    public IReadOnlyList<CardCombination> CandidatesFor(Seat seat) =>
        PendingFor(seat) is PendingDecision.NeedPlay play ? play.Candidates : [];

    /// <summary>能不能撤回。除了栈非空，还要求停在出牌阶段。</summary>
    public bool CanUndo => _undoStack.Count > 0 && _engine.Phase == GamePhase.Playing;

    public SeatKind KindOf(Seat seat) => _seatKinds.TryGetValue(seat, out var kind) ? kind : SeatKind.Ai;

    /// <summary>状态有任何变化，界面重画。</summary>
    public event Action? Changed;

    /// <summary>轮到人做决定了（叫主 / 反主 / 换底牌 / 出牌）。</summary>
    public event Action? HumanTurnChanged;

    /// <summary>读秒跳了一格。界面只需要重画那一处倒计时。</summary>
    public event Action? CountdownChanged;

    /// <summary>托管开关变了。</summary>
    public event Action? AutoPlayChanged;

    /// <summary>一局打完。</summary>
    public event Action<RoundSummary>? RoundFinished;

    // ---------------------------------------------------------------- 对外命令

    /// <summary>启动驱动循环。第一次调用会自动开第一局。</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _loop ??= RunAsync();

        lock (_gate)
        {
            if (_engine.Phase == GamePhase.NotStarted)
            {
                BeginRound();
            }
            else
            {
                Signal();
            }
        }
    }

    public void StartNextRound()
    {
        lock (_gate)
        {
            if (_engine.Phase is GamePhase.RoundOver)
            {
                BeginRound();
            }
        }
    }

    public void SubmitCall(CallOption? option) => SubmitCall(HumanSeat, option);

    public void SubmitCall(Seat seat, CallOption? option)
    {
        lock (_gate)
        {
            if (_humanPending is not PendingDecision.NeedCall needCall || needCall.Actor != seat)
            {
                return;
            }

            if (option is null && !needCall.CanPass)
            {
                return;
            }

            if (option is not null && !needCall.Options.Contains(option))
            {
                // 拿一个不在当前候选里的叫法（过期消息、或者有人在手搓协议）。
                return;
            }

            var command = option is null
                ? (GameCommand)new PassCall(seat)
                : new DeclareCall(seat, option);

            Submit(command);
        }
    }

    public void SubmitBottomDiscards(IReadOnlyList<Card> discards) => SubmitBottomDiscards(HumanSeat, discards);

    public void SubmitBottomDiscards(Seat seat, IReadOnlyList<Card> discards)
    {
        lock (_gate)
        {
            if (_humanPending is not PendingDecision.NeedBottomSwap swap || swap.Actor != seat)
            {
                return;
            }

            // ⚠️ 这些检查必须在这儿做。引擎的 Apply 是「边改边判」的：
            // 抛异常的时候 Pending 已经被清掉了，整局就此卡死、再也推不动。
            // 所以非法输入一律在进引擎之前挡掉，只驳回、不推进。
            var reason = CheckBottomDiscards(swap, discards);

            if (reason is not null)
            {
                Status = reason;
                Changed?.Invoke();
                return;
            }

            Submit(new CommitBottom(seat, [.. discards]));
        }
    }

    public void SubmitPlay(IReadOnlyList<Card> cards) => SubmitPlay(HumanSeat, cards);

    public void SubmitPlay(Seat seat, IReadOnlyList<Card> cards)
    {
        lock (_gate)
        {
            if (_humanPending is not PendingDecision.NeedPlay play || play.Actor != seat)
            {
                return;
            }

            // 同上：非法的一手绝不喂给引擎。
            var check = ValidateSelection(seat, cards);

            if (!check.IsValid)
            {
                Status = check.Reason;
                Changed?.Invoke();
                return;
            }

            // 撤回只对"本机那个座位"有意义：联机时退别人的牌，是另一码事。
            var undoable = seat == HumanSeat;

            if (undoable)
            {
                PushUndo();
            }

            if (!Submit(new PlayCards(seat, [.. cards])) && undoable)
            {
                // 这一手没生效，撤回点也不能留着。
                _undoStack.RemoveAt(_undoStack.Count - 1);
            }
        }
    }

    public void ApplyOptions(RuleOptions options)
    {
        lock (_gate)
        {
            Options = options;
            _engine.UpdateOptions(options);
            Status = "规则已改，从下一局开始生效";
            Changed?.Invoke();
            Signal();
        }
    }

    /// <summary>换一批脑子（改人机难度用）。下一个人做决策就按新的走，不用重开一盘。</summary>
    public void UpdateBrains(IReadOnlyDictionary<Seat, IPlayerBrain> brains)
    {
        lock (_gate)
        {
            _brains = brains;
        }
    }

    public void SetSeatKind(Seat seat, SeatKind kind)
    {
        lock (_gate)
        {
            _seatKinds[seat] = kind;
            Signal();
        }
    }

    /// <summary>
    /// 交出去 / 收回来。正卡在等真人出手的时候切过去，会立刻推一把，
    /// 不用等下一次节拍——不然点了「托管」还得干等。
    /// </summary>
    public void SetAutoPlay(bool on) => SetAutoPlay(HumanSeat, on);

    public void SetAutoPlay(Seat seat, bool on)
    {
        lock (_gate)
        {
            SetAutoPlayLocked(seat, on);
        }
    }

    private void SetAutoPlayLocked(Seat seat, bool on)
    {
        if (IsAutoPlay(seat) == on)
        {
            return;
        }

        _autoPlay[seat] = on;

        if (seat == HumanSeat)
        {
            StopHumanCountdown();
            _humanPending = null;

            Status = on
                ? "已交给电脑代打，点「收回托管」随时收回来"
                : "收回托管，还是你自己打";
        }

        AutoPlayChanged?.Invoke();
        Changed?.Invoke();
        Signal();
    }

    /// <summary>撤回自己上一手出的牌。</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            return UndoLocked();
        }
    }

    private bool UndoLocked()
    {
        if (!CanUndo)
        {
            return false;
        }

        _pauseMs = 0;
        StopHumanCountdown();

        var point = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _engine.RestoreState(point.Engine);

        if (_log.Count > point.LogCount)
        {
            _log.RemoveRange(point.LogCount, _log.Count - point.LogCount);
        }

        _lastTrickPlays = point.LastTrickPlays;
        _shownCards = point.ShownCards;
        _shownCardsSeat = point.ShownCardsSeat;
        _roundDeclarerTeam = point.RoundDeclarerTeam;
        _roomJustFinished = point.RoomJustFinished;

        _humanPending = _engine.Pending;
        Status = "已撤回上一手，请重新出牌";

        Changed?.Invoke();
        HumanTurnChanged?.Invoke();
        Signal();
        return true;
    }

    /// <summary>问一下这手牌能不能出，不能出就把原因给出来，显示在状态栏上。</summary>
    public ValidationResult ValidateSelection(IReadOnlyList<Card> cards) => ValidateSelection(HumanSeat, cards);

    public ValidationResult ValidateSelection(Seat seat, IReadOnlyList<Card> cards)
    {
        if (_humanPending is not PendingDecision.NeedPlay play || play.Actor != seat)
        {
            return ValidationResult.Fail("现在不该你出牌");
        }

        if (cards.Count == 0)
        {
            return ValidationResult.Fail(play.Lead is null ? "选牌领出" : $"要跟 {play.Lead.Describe()}");
        }

        var trump = _engine.Trump!;

        // 领出交给引擎判：甩牌还要看这门里别人手上有没有更大的牌。
        if (play.Lead is null)
        {
            return _engine.ValidateLead(seat, cards);
        }

        return MoveValidator.Validate(play.Lead, _engine.HandOf(seat), cards, trump);
    }

    public TableSnapshot Snapshot()
    {
        return new TableSnapshot
        {
            // 本轮有人出过牌就显示本轮的；刚打完还没人出，就把上一轮留一会儿。
            Trick = _engine.CurrentTrick.Count > 0 ? [.. _engine.CurrentTrick] : [.. _lastTrickPlays],
            HumanSeat = HumanSeat,
            TrumpText = _engine.Trump?.Describe() ?? "还没定主",
            LevelText = _engine.HasDeclarer
                ? $"{_engine.RankOf(_engine.DeclarerTeam).Text()}（{_engine.DeclarerTeam.Name()}）"
                : "还没定",
            DeclarerTeam = _engine.DeclarerTeam,
            DeclarerSeat = _engine.HasDeclarer ? _engine.DeclarerSeat : null,
            DefenderPoints = _engine.DefenderTrickPoints,
            DeclarerPoints = _engine.DeclarerTrickPoints,
            RemainingCounts = [.. Seats.All.Select(seat => _engine.HandOf(seat).Count)],
            TrickNumber = Math.Min(_engine.TrickNumber + 1, CardsPerSeat),
            Bottom = [.. _engine.Bottom],
            BottomRevealed = _engine.Phase is GamePhase.Settling or GamePhase.RoundOver or GamePhase.RoomOver,
            ShownCards = _shownCards,
            ShownCardsSeat = _shownCardsSeat,
            Status = Status,
        };
    }

    /// <summary>
    /// 开一个"房间"：每个座位是谁（真人/AI）由调用方定，联机服务端用这个。
    /// 和单人局的区别只有一个——**几个座位在等人**，而不是固定的那一家。
    /// </summary>
    public static GameSession CreateForRoom(
        IReadOnlyDictionary<Seat, SeatKind> seatKinds,
        RuleOptions? options = null,
        int? seed = null,
        AiLevel aiLevel = AiLevel.Normal)
    {
        options ??= RuleOptions.Default;

        IRandomSource rng = new SeededRandomSource(seed ?? Random.Shared.Next());
        var engine = new GameEngine(rng, options);
        var brains = Seats.All.ToDictionary(seat => seat, _ => BrainFactory.Create(aiLevel, rng));

        var session = new GameSession(engine, brains, Seat.South, options);

        foreach (var seat in Seats.All)
        {
            session.SetSeatKind(seat, seatKinds.TryGetValue(seat, out var kind) ? kind : SeatKind.Ai);
        }

        return session;
    }

    /// <summary>开一盘单人局：一家真人、三家电脑。</summary>
    public static GameSession CreateSinglePlayer(
        AiLevel level,
        RuleOptions? options = null,
        Seat humanSeat = Seat.South,
        int? seed = null)
    {
        options ??= RuleOptions.Default;

        IRandomSource rng = new SeededRandomSource(seed ?? Random.Shared.Next());
        var engine = new GameEngine(rng, options);
        var brains = Seats.All.ToDictionary(seat => seat, _ => BrainFactory.Create(level, rng));

        return new GameSession(engine, brains, humanSeat, options);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        Signal();
        _cts.Dispose();
    }

    // ---------------------------------------------------------------- 驱动循环

    private async Task RunAsync()
    {
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // 进来先把多余的唤醒信号丢掉，只按当前局面重新算下一步。
                while (_wake.Wait(0))
                {
                }

                Step step;
                lock (_gate)
                {
                    step = Plan();
                }

                switch (step.Kind)
                {
                    case StepKind.None:
                        await _wake.WaitAsync(token);
                        continue;

                    case StepKind.HumanTick:
                        if (await _wake.WaitAsync(step.DelayMs, token))
                        {
                            continue;
                        }

                        lock (_gate)
                        {
                            TickHumanCountdown();
                        }

                        continue;
                }

                if (step.DelayMs > 0 && await _wake.WaitAsync(step.DelayMs, token))
                {
                    // 等节拍的时候被人操作打断了，重算。
                    continue;
                }

                lock (_gate)
                {
                    Execute(step.Action);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常收摊。
        }
        catch (Exception ex)
        {
            Status = $"出错：{ex.Message}";
            _log.Add($"✗ {ex.Message}");
            Changed?.Invoke();
        }
    }

    /// <summary>算出"下一步该干什么、先等多久"。不推进局面，只是排计划。</summary>
    private Step Plan()
    {
        // 上一墩的待决策已经处理完了就把"等人"的状态收掉。
        if (_humanPending is not null && !ReferenceEquals(_engine.Pending, _humanPending))
        {
            _humanPending = null;
            StopHumanCountdown();
        }

        // 一轮四人出完，停一下让人看清这轮出了什么牌。
        if (_pauseMs > 0)
        {
            return Step.Pause(_pauseMs);
        }

        if (_engine.Phase is GamePhase.RoundOver or GamePhase.RoomOver)
        {
            AnnounceRoundFinished();
            return Step.Wait;
        }

        if (_engine.Pending is not { } pending)
        {
            // 还在发牌（首局/抢主局的"抢先叫主"窗口），按节拍继续推。
            if (_engine.Phase == GamePhase.Dealing)
            {
                Status = "发牌中……（发牌途中谁先叫得出来谁坐庄）";
                Changed?.Invoke();
                return Step.Deal(DealRoundMs);
            }

            return Step.Wait;
        }

        if (IsHumanSeat(pending.Actor) && !IsAutoPlay(pending.Actor))
        {
            if (_humanPending is null)
            {
                _humanPending = pending;
                Status = Prompt(pending);
                StartHumanCountdown(pending);
                HumanTurnChanged?.Invoke();
                Changed?.Invoke();
            }
            else if (_humanSecondsLeft <= 0 && IsTimed(pending))
            {
                // 「撤回」这类路径会绕过上面那段直接设 _humanPending。
                // 读秒要是不重新起，下一秒就会被判成超时，电脑立刻替你出牌——
                // 表现就是"一撤回，牌自己又打出去了"。
                StartHumanCountdown(pending);
            }

            return IsTimed(pending) ? Step.HumanTick : Step.Wait;
        }

        return Step.Ai(AiThinkDelayMs);
    }

    private void Execute(ActionKind action)
    {
        switch (action)
        {
            case ActionKind.Pause:
                _pauseMs = 0;
                break;

            case ActionKind.Deal:
                Apply(_engine.AdvanceDeal());
                break;

            case ActionKind.Ai:
                ExecuteAi();
                break;
        }
    }

    private void ExecuteAi()
    {
        if (_engine.Pending is not { } pending)
        {
            return;
        }

        if (IsHumanSeat(pending.Actor) && !IsAutoPlay(pending.Actor))
        {
            return;
        }

        if (!_brains.TryGetValue(pending.Actor, out var brain))
        {
            return;
        }

        var command = BrainDecider.Decide(
            brain,
            pending,
            _engine.Trump,
            BrainDecider.Describe(_engine, pending.Actor));

        Apply(_engine.Apply(command));
    }

    private void BeginRound()
    {
        _roundFinishedFired = false;
        Apply(_engine.StartRound());
        Signal();
    }

    /// <summary>换底牌要先过这一关：正好 8 张、都在可挑范围里、而且不含叫主明牌。都合法才返回 null。</summary>
    private static string? CheckBottomDiscards(PendingDecision.NeedBottomSwap swap, IReadOnlyList<Card> discards)
    {
        if (discards.Count != 8)
        {
            return $"换底牌要扣 8 张，你给了 {discards.Count} 张";
        }

        var pool = swap.Pool;

        if (discards.Any(card => !pool.Contains(card)))
        {
            return "扣的牌不在可挑范围里";
        }

        if (discards.Any(card => swap.Locked.Contains(card)))
        {
            return "叫主摆出来的明牌不能扣进底牌";
        }

        return null;
    }

    private bool Submit(GameCommand command)
    {
        lock (_gate)
        {
            StopHumanCountdown();
            _humanPending = null;

            try
            {
                Apply(_engine.Apply(command));
                Signal();
                return true;
            }
            catch (Exception ex)
            {
                // 非法动作不应该走到这儿（界面先校验），真到了也不能把整局带崩。
                _humanPending = _engine.Pending;
                Status = ex.Message;
                Changed?.Invoke();
                return false;
            }
        }
    }

    private void Apply(IReadOnlyList<GameEvent> events)
    {
        RecordEvents(events);
        _pauseMs = events.Any(e => e is TrickWon) ? TrickPauseMs : 0;
        Changed?.Invoke();
    }

    private void AnnounceRoundFinished()
    {
        if (_roundFinishedFired)
        {
            return;
        }

        _roundFinishedFired = true;

        Status = _roomJustFinished
            ? $"{_engine.RoomWinner.Name()} 打到 A 又赢一局，这盘结束了"
            : $"第 {_engine.RoundNumber} 局打完";

        Changed?.Invoke();
        RoundFinished?.Invoke(BuildSummary());
    }

    // ---------------------------------------------------------------- 读秒

    /// <summary>
    /// 哪几步要读秒。出牌和叫主/反主都要；**换底牌不读**——
    /// 那一步真的要自己拿主意，电脑替你扣 8 张下去后果太重。
    /// </summary>
    private static bool IsTimed(PendingDecision pending) =>
        pending is PendingDecision.NeedPlay or PendingDecision.NeedCall;

    private void StartHumanCountdown(PendingDecision pending)
    {
        StopHumanCountdown();

        // 至少 1 秒：万一有人把超时设成 0，循环会每次都"重新起表"，变成死转。
        _humanSecondsLeft = pending switch
        {
            PendingDecision.NeedPlay => Math.Max(1, HumanTimeoutSeconds),
            PendingDecision.NeedCall => Math.Max(1, CallTimeoutSeconds),
            _ => 0,
        };
    }

    private void StopHumanCountdown() => _humanSecondsLeft = 0;

    private void TickHumanCountdown()
    {
        if (_humanPending is not { } pending || !IsTimed(pending))
        {
            StopHumanCountdown();
            return;
        }

        _humanSecondsLeft--;

        if (_humanSecondsLeft > 0)
        {
            CountdownChanged?.Invoke();
            return;
        }

        StopHumanCountdown();

        // 超时了，用你自己座位那副脑子替你走这一步。
        // 出牌走 SubmitPlay，所以照样能按「撤回」退回来。
        var seconds = pending is PendingDecision.NeedPlay ? HumanTimeoutSeconds : CallTimeoutSeconds;

        _log.Add(pending is PendingDecision.NeedPlay
            ? $"⚠ 超过 {seconds} 秒没动作，电脑替你出了一手"
            : $"⚠ 超过 {seconds} 秒没动作，电脑替你叫了");

        var actor = pending.Actor;

        var command = BrainDecider.Decide(
            _brains[actor],
            pending,
            _engine.Trump,
            BrainDecider.Describe(_engine, actor));

        switch (command)
        {
            case PlayCards play:
                SubmitPlay(actor, play.Cards);
                break;

            case DeclareCall declare:
                SubmitCall(actor, declare.Option);
                break;

            case PassCall:
                SubmitCall(actor, null);
                break;

            case CommitBottom commit:
                Submit(commit);
                break;
        }
    }

    // ---------------------------------------------------------------- 记录

    private void PushUndo() => _undoStack.Add(new UndoPoint(
        _engine.CaptureState(),
        _log.Count,
        _lastTrickPlays,
        _shownCards,
        _shownCardsSeat,
        _roundDeclarerTeam,
        _roomJustFinished,
        Status));

    private void RecordEvents(IReadOnlyList<GameEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        _roomJustFinished = false;

        foreach (var gameEvent in events)
        {
            _log.Add(gameEvent.Describe());
            Status = gameEvent.Describe();

            switch (gameEvent)
            {
                case RoundStarted:
                    _lastTrickPlays = [];

                    // 撤回只在本局内有效，开新的一局就作废。
                    _undoStack.Clear();
                    break;

                case TrickWon won:
                    _lastTrickPlays = won.Plays;
                    break;

                case RoundSettled settled:
                    _lastSettled = settled;
                    break;

                case BottomRevealed bottom:
                    _lastBottom = bottom;
                    break;

                case LevelsChanged levels:
                    _lastLevels = levels;
                    break;

                case RoomFinished:
                    _roomJustFinished = true;
                    break;

                // 叫主摆出来的明牌，摊在桌上给所有人看（文档第 56 行）。
                case TrumpEstablished:
                    _shownCards = [.. _engine.Locked];
                    _shownCardsSeat = _engine.DeclarerSeat;
                    _roundDeclarerTeam = _engine.DeclarerTeam;
                    break;

                // 正式开打，明牌收回去。
                case CardPlayed when _shownCards.Count > 0:
                    _shownCards = [];
                    _shownCardsSeat = null;
                    break;
            }
        }

        // 出牌阶段每一手都刷状态栏太吵，只留最后一手。
        if (events.Count > 1 && events[^1] is CardPlayed)
        {
            Status = string.Empty;
        }

        if (_log.Count > 4000)
        {
            _log.RemoveRange(0, _log.Count - 4000);
        }
    }

    private RoundSummary BuildSummary()
    {
        var before = _lastLevels is { } lv
            ? $"东西 {LevelTrack.Describe(lv.Before[0])} / 南北 {LevelTrack.Describe(lv.Before[1])}"
            : "-";

        var after = _lastLevels is { } la
            ? $"东西 {LevelTrack.Describe(la.After[0])} / 南北 {LevelTrack.Describe(la.After[1])}"
            : "-";

        // 这一局的庄家要用开局时记下的那个，不能用 _engine.DeclarerTeam（那已经是下一局的了）。
        var declarerLevel = _lastLevels is { } dl
            ? $"本局 {_roundDeclarerTeam.Name()} 坐庄、{_roundDeclarerTeam.Other().Name()} 是闲家："
              + $"{LevelTrack.Describe(dl.Before[(int)_roundDeclarerTeam])} → {LevelTrack.Describe(dl.After[(int)_roundDeclarerTeam])}"
            : "-";

        var next = _engine.Phase == GamePhase.RoomOver
            ? $"{_engine.RoomWinner.Name()} 打到 A 又赢了一局，整盘结束，可以重新开一盘"
            : $"下一局由 {_engine.DeclarerTeam.Name()} 坐庄，打 {_engine.RankOf(_engine.DeclarerTeam).Text()}";

        return new RoundSummary
        {
            RoundNumber = _engine.RoundNumber,
            OutcomeText = _lastSettled is { } settled
                ? RoundResultEvaluator.Describe(settled.Outcome)
                : "本局结束",
            DefenderPoints = _lastSettled?.DefenderPoints ?? 0,
            DeclarerPoints = _engine.DeclarerTrickPoints,
            Bottom = _lastBottom?.Cards ?? [],
            BottomText = _lastBottom?.Result.Describe() ?? string.Empty,
            LevelBefore = $"{before}（{declarerLevel}）",
            LevelAfter = after,
            NextRoundText = next,
            RoomOver = _engine.Phase == GamePhase.RoomOver,
        };
    }

    private string Prompt(PendingDecision pending) => pending switch
    {
        PendingDecision.NeedCall { IsCounter: true } => $"轮到你反主（{CallTimeoutSeconds} 秒内不定就替你定）",
        PendingDecision.NeedCall => $"轮到你叫主（{CallTimeoutSeconds} 秒内不定就替你定）",
        PendingDecision.NeedBottomSwap => "轮到你换底牌，挑 8 张扣回去",
        PendingDecision.NeedPlay { Lead: null } => "轮到你领出，选牌后点「出牌」",
        PendingDecision.NeedPlay play =>
            $"轮到你跟牌：这一手是 {play.Lead!.Describe()}，要出 {play.Lead.Length} 张",
        _ => string.Empty,
    };

    private void Signal()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已经有一个待处理的信号了，多余的丢掉就行。
        }
    }
}
