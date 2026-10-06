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
using FanKou.Ui.Controls;
using FanKou.Ui.Dialogs;

namespace FanKou.App;

/// <summary>
/// 推进对局：轮到人就等人操作，轮到 AI 就隔一小会儿自己出牌。
/// 界面只读它给的状态快照，不自己维护游戏状态——这是 WinForms 游戏最常见的一类 bug。
/// </summary>
public sealed class GameController : IDisposable
{
    /// <summary>电脑"思考"多久再出牌。</summary>
    private const int AiThinkDelayMs = 620;

    /// <summary>一轮四人出完牌之后停多久，让人看清这一轮出了什么。</summary>
    private const int TrickPauseMs = 1500;

    /// <summary>
    /// 发牌时每发出一轮（四个人各一张）花多久。
    /// 整副 25 轮 × 0.5 秒 = 12.5 秒。这个窗口是留给"发牌途中抢主"的。
    /// </summary>
    private const int DealRoundMs = 500;

    private const int CardsPerSeat = 25;

    private IReadOnlyDictionary<Seat, IPlayerBrain> _brains;
    private readonly System.Windows.Forms.Timer _aiTimer = new();
    private readonly System.Windows.Forms.Timer _pauseTimer = new();
    private readonly System.Windows.Forms.Timer _dealTimer = new();
    private readonly List<string> _log = [];

    private GameEngine _engine;
    private PendingDecision? _humanPending;

    private RoundSettled? _lastSettled;
    private BottomRevealed? _lastBottom;
    private LevelsChanged? _lastLevels;

    // 刚打完那一局的庄家是谁。结算之后 _engine.DeclarerTeam 已经换成下一局的了，
    // 所以要在叫主定下来的当时记一份，不然结算框会把庄闲说反。
    private Team _roundDeclarerTeam;

    private bool _roomJustFinished;

    private IReadOnlyList<Card> _shownCards = [];
    private Seat? _shownCardsSeat;

    // 一轮打完引擎就把出牌清空了，但暂停那会儿还得让人看得见刚才出了什么，
    // 所以这里留一份最近一轮的。
    private ImmutableArray<TrickPlay> _lastTrickPlays = [];

    /// <summary>
    /// 撤回栈。每按一次「撤回」弹一层，退回用户上一手出牌之前。
    /// 开新的一局会清空——撤回只在本局内有效，跨局要连等级、庄闲一起回滚，不值当。
    /// </summary>
    private readonly List<UndoPoint> _undoStack = [];

    /// <summary>
    /// 一个撤回点 = 引擎快照 + 界面自己派生出来的那点状态。
    ///
    /// 后半截不能省：引擎还原了，<c>_log</c> 这些**不会**跟着回滚。
    /// 日志尤其明显——它是只追加的，不截断的话撤回之后记录里还留着
    /// 那些"已经没发生过"的牌，对不上。
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

    public GameController(
        GameEngine engine,
        IReadOnlyDictionary<Seat, IPlayerBrain> brains,
        Seat humanSeat,
        RuleOptions options)
    {
        _engine = engine;
        _brains = brains;
        HumanSeat = humanSeat;
        Options = options;

        _aiTimer.Interval = AiThinkDelayMs;
        _aiTimer.Tick += OnAiTick;

        _pauseTimer.Interval = TrickPauseMs;
        _pauseTimer.Tick += OnPauseTick;

        _dealTimer.Interval = DealRoundMs;
        _dealTimer.Tick += OnDealTick;
    }

    public Seat HumanSeat { get; }

    /// <summary>当前的规则开关。改了之后**下一局生效**，正在打的不受影响。</summary>
    public RuleOptions Options { get; private set; }

    public void ApplyOptions(RuleOptions options)
    {
        Options = options;
        _engine.UpdateOptions(options);
        Status = "规则已改，从下一局开始生效";
        Changed?.Invoke();
    }

    /// <summary>换一批脑子（改人机难度用）。下一个人做决策就按新的走，不用重开一盘。</summary>
    public void UpdateBrains(IReadOnlyDictionary<Seat, IPlayerBrain> brains) => _brains = brains;

    /// <summary>开发自检用：人类座位也交给 AI，跑起来不需要人操作。</summary>
    public bool AutoPlayHuman { get; init; }

    public GameEngine Engine => _engine;

    public IReadOnlyList<string> Log => _log;

    public string Status { get; private set; } = "点「开始新的一局」开局";

    public PendingDecision? HumanPending => _humanPending;

    /// <summary>正在一张一张发牌。这期间不接受出牌操作，点一下可以跳过。</summary>
    public bool IsDealing => _engine.Phase == GamePhase.Dealing;

    /// <summary>轮到人出牌时，当前所有合法候选（给"提示"用）。</summary>
    public IReadOnlyList<CardCombination> HumanCandidates =>
        _humanPending is PendingDecision.NeedPlay play ? play.Candidates : [];

    /// <summary>自己的手牌。发牌途中是发到哪张就露哪张（引擎手里本来就是半副牌）。</summary>
    public IReadOnlyList<Card> HumanHand => _engine.HandOf(HumanSeat);

    /// <summary>状态有任何变化，界面重画。</summary>
    public event Action? Changed;

    /// <summary>轮到人做决定了（叫主 / 换底牌 / 出牌）。</summary>
    public event Action? HumanTurnChanged;

    /// <summary>一局打完，窗体弹结算框。</summary>
    public event Action<RoundSummary>? RoundFinished;

    public void Start()
    {
        if (_engine.Phase == GamePhase.NotStarted)
        {
            AdvanceTo(_engine.StartRound());
        }
    }

    public void StartNextRound()
    {
        if (_engine.Phase is GamePhase.RoundOver)
        {
            AdvanceTo(_engine.StartRound());
        }
    }

    public void SubmitCall(CallOption? option)
    {
        if (_humanPending is not PendingDecision.NeedCall needCall)
        {
            return;
        }

        if (option is null && !needCall.CanPass)
        {
            return;
        }

        var command = option is null
            ? (GameCommand)new PassCall(HumanSeat)
            : new DeclareCall(HumanSeat, option);

        AdvanceTo(_engine.Apply(command));
    }

    public void SubmitBottomDiscards(IReadOnlyList<Card> discards)
    {
        if (_humanPending is PendingDecision.NeedBottomSwap && discards.Count == 8)
        {
            AdvanceTo(_engine.Apply(new CommitBottom(HumanSeat, [.. discards])));
        }
    }

    public void SubmitPlay(IReadOnlyList<Card> cards)
    {
        if (_humanPending is PendingDecision.NeedPlay)
        {
            // 先存档再出牌：这一份就是「撤回」能退回去的那个点，
            // 存的是"已经轮到你了、牌还没出手"的状态。
            _undoStack.Add(new UndoPoint(
                _engine.CaptureState(),
                _log.Count,
                _lastTrickPlays,
                _shownCards,
                _shownCardsSeat,
                _roundDeclarerTeam,
                _roomJustFinished,
                Status));

            AdvanceTo(_engine.Apply(new PlayCards(HumanSeat, [.. cards])));
        }
    }

    /// <summary>
    /// 能不能撤回。除了栈非空，还要求停在出牌阶段——一局已经结算掉之后再退回去，
    /// 等级和庄闲都已经变了，那是另一码事，这次不做。
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0 && _engine.Phase == GamePhase.Playing;

    /// <summary>
    /// 撤回自己上一手出的牌。
    ///
    /// 退到"你出牌之前"：东、北、西已经出的三张留在桌上，你那一手、这一墩的计分结算、
    /// 以及之后已经走掉的牌全部撤销，回到轮到你重新出牌。
    ///
    /// 可以连按，一次退一墩，一直退到本局开头。因为撤回点是"随时"能按的，
    /// 按下去的时候 AI 的思考计时/一墩打完的暂停/发牌节拍都可能正跑着，先统统停掉。
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _aiTimer.Stop();
        _pauseTimer.Stop();
        _dealTimer.Stop();

        var point = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _engine.RestoreState(point.Engine);

        // 引擎回去了，界面这层派生状态得手动收。
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
        return true;
    }

    /// <summary>问一下这手牌能不能出，不能出就把原因给出来，显示在状态栏上。</summary>
    public ValidationResult ValidateSelection(IReadOnlyList<Card> cards)
    {
        if (_humanPending is not PendingDecision.NeedPlay play)
        {
            return ValidationResult.Fail("现在不该你出牌");
        }

        if (cards.Count == 0)
        {
            return ValidationResult.Fail(
                play.Lead is null ? "选牌领出" : $"要跟 {play.Lead.Describe()}");
        }

        var trump = _engine.Trump!;

        // 领出交给引擎判：甩牌还要看这门里别人手上有没有更大的牌。
        if (play.Lead is null)
        {
            return _engine.ValidateLead(HumanSeat, cards);
        }

        return MoveValidator.Validate(play.Lead, _engine.HandOf(HumanSeat), cards, trump);
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

    public void Dispose()
    {
        _aiTimer.Stop();
        _aiTimer.Dispose();
        _pauseTimer.Stop();
        _pauseTimer.Dispose();
        _dealTimer.Stop();
        _dealTimer.Dispose();
    }

    private void AdvanceTo(IReadOnlyList<GameEvent> events)
    {
        RecordEvents(events);

        // 一轮四人出完，停一下让人看清这轮出了什么牌，再进下一轮。
        if (events.Any(e => e is TrickWon))
        {
            _pauseTimer.Stop();
            _pauseTimer.Start();
            Changed?.Invoke();
            return;
        }

        Pump();
    }

    /// <summary>
    /// 按节拍推一轮发牌。这中间如果谁先叫得出来（抢主），引擎会给出待决策，
    /// 我们就停下来先处理叫主，处理完接着发。
    /// </summary>
    private void OnDealTick(object? sender, EventArgs e)
    {
        if (_engine.Phase != GamePhase.Dealing || _engine.Pending is not null)
        {
            _dealTimer.Stop();
            Pump();
            return;
        }

        RecordEvents(_engine.AdvanceDeal());
        Changed?.Invoke();
    }

    private void OnPauseTick(object? sender, EventArgs e)
    {
        _pauseTimer.Stop();
        Pump();
    }

    private void Pump()
    {
        _humanPending = null;

        if (_engine.Phase is GamePhase.RoundOver or GamePhase.RoomOver)
        {
            Status = _roomJustFinished
                ? $"{_engine.RoomWinner.Name()} 打到 A 又赢一局，这盘结束了"
                : $"第 {_engine.RoundNumber} 局打完";

            Changed?.Invoke();
            RoundFinished?.Invoke(BuildSummary());
            return;
        }

        if (_engine.Pending is not { } pending)
        {
            // 还在发牌（首局/抢主局的"抢先叫主"窗口），按节拍继续推。
            // 刻意不做"跳过"：玩家局里要四个人都同意才能跳，反而麻烦，
            // 而且抢主就发生在发牌这一段，跳过了就没意义了。
            if (_engine.Phase == GamePhase.Dealing)
            {
                Status = "发牌中……（发牌途中谁先叫得出来谁坐庄）";
                Changed?.Invoke();
                _dealTimer.Stop();
                _dealTimer.Start();
                return;
            }

            Changed?.Invoke();
            return;
        }

        if (pending.Actor == HumanSeat && !AutoPlayHuman)
        {
            _humanPending = pending;
            Status = Prompt(pending);
            HumanTurnChanged?.Invoke();
            Changed?.Invoke();
            return;
        }

        Changed?.Invoke();
        _aiTimer.Stop();
        _aiTimer.Start();
    }

    private void OnAiTick(object? sender, EventArgs e)
    {
        _aiTimer.Stop();

        if (_engine.Pending is not { } pending
            || (pending.Actor == HumanSeat && !AutoPlayHuman))
        {
            return;
        }

        var command = BrainDecider.Decide(
            _brains[pending.Actor],
            pending,
            _engine.Trump,
            BrainDecider.Describe(_engine));

        AdvanceTo(_engine.Apply(command));
    }

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

    private static string Prompt(PendingDecision pending) => pending switch
    {
        PendingDecision.NeedCall { IsCounter: true } => "轮到你反主（也可以不反）",
        PendingDecision.NeedCall => "轮到你叫主",
        PendingDecision.NeedBottomSwap => "轮到你换底牌，挑 8 张扣回去",
        PendingDecision.NeedPlay { Lead: null } => "轮到你领出，选牌后点「出牌」",
        PendingDecision.NeedPlay play =>
            $"轮到你跟牌：这一手是 {play.Lead!.Describe()}，要出 {play.Lead.Length} 张",
        _ => string.Empty,
    };
}
