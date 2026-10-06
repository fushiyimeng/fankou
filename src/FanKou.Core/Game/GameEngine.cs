using System.Collections.Immutable;
using FanKou.Core.Bidding;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Config;
using FanKou.Core.Levels;
using FanKou.Core.Play;
using FanKou.Core.Scoring;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Core.Game;

/// <summary>
/// 一个房间的全部流程：发牌 → 叫主 → 换底牌 → 出牌 → 结算 → 下一局。
///
/// 用法就是一个循环：<c>Pending</c> 为 null 时引擎自己往下走，
/// 不为 null 时把决策交给真人或 AI，拿到命令再 <see cref="Apply"/> 回来。
/// 全程不依赖任何界面，所以可以拿来跑上万局自检。
/// </summary>
public sealed class GameEngine
{
    private const int MaxRedeals = 200;

    /// <summary>每人 25 张，四个人一共 100 张进手；剩下 8 张做锅底。</summary>
    private const int CardsPerSeat = 25;

    private const int TotalDealt = CardsPerSeat * 4;

    private readonly IRandomSource _rng;
    private RuleOptions _options;

    private readonly List<Card>[] _hands = [[], [], [], []];
    private List<Card> _bottom = [];
    private Card[] _locked = [];

    // 首局一张一张发牌用。发牌是顺时针，跟出牌的逆时针是反的。
    private List<Card> _deck = [];
    private int _dealCursor;
    private Seat _dealStart = Seat.East;
    private bool _slowDeal;
    private bool _calledDuringDeal;
    private CallOption? _acceptedOption;

    private readonly int[] _levels = [0, 0];

    // 开局那一刻的等级。结算里要拿它做"变之前"，这样叫主阶段掉级之类的事也能一起体现出来。
    private readonly int[] _levelsAtRoundStart = [0, 0];
    private Team _declarerTeam = Team.EastWest;
    private bool _hasDeclarer;
    private int _roundNumber;
    private Seat _declarerSeat;

    private TrumpContext? _trump;
    private Rank _levelRank;

    // 叫主阶段
    private List<Seat> _callQueue = [];
    private int _callIndex;
    private CallRound _callRound;
    private readonly HashSet<Seat> _passedCall = [];
    private int _redeals;

    // 反主阶段
    private CallOption? _highestCall;
    private Seat _highestCaller;
    private List<Seat> _counterQueue = [];
    private int _counterIndex;
    private int _counterCount;

    /// <summary>这一局的反主是在换底牌**之前**问的（只有首局是这样，文档第 88 行）。</summary>
    private bool _counterBeforeSwap;

    /// <summary>首局：还没有庄家，谁最后叫/反成功谁是庄家（文档第 44 行）。</summary>
    private bool _isFirstRound;

    /// <summary>这一局是抢主局（上一局闲家拿了 85~114 分）。</summary>
    private bool _isGrabRound;

    /// <summary>下一局要不要走抢主。</summary>
    private bool _nextRoundIsGrab;

    /// <summary>首局和抢主局里，各家用**自己队**的等级叫牌（文档第 86、93 行）。</summary>
    private bool _perTeamLevel;

    // 出牌阶段
    private readonly List<TrickPlay> _trickPlays = [];
    private readonly List<TrickRecord> _history = [];
    private Seat _leader;
    private int _defenderTrickPoints;
    private int _declarerTrickPoints;
    private ComboType _lastTrickType = ComboType.Invalid;
    private Team _lastTrickWinnerTeam;
    private int _trickNumber;

    public GameEngine(IRandomSource rng, RuleOptions? options = null)
    {
        _rng = rng;
        _options = options ?? RuleOptions.Default;
    }

    public GamePhase Phase { get; private set; } = GamePhase.NotStarted;

    public PendingDecision? Pending { get; private set; }

    public Team RoomWinner { get; private set; }

    public int RoundNumber => _roundNumber;

    public Team DeclarerTeam => _declarerTeam;

    public Seat DeclarerSeat => _declarerSeat;

    public bool HasDeclarer => _hasDeclarer;

    public TrumpContext? Trump => _trump;

    public Rank LevelRank => _levelRank;

    public IReadOnlyList<TrickPlay> CurrentTrick => _trickPlays;

    /// <summary>已经打完的每一轮。公开信息，AI 记牌就靠它。</summary>
    public IReadOnlyList<TrickRecord> History => _history;

    public int TrickNumber => _trickNumber;

    /// <summary>闲家靠赢牌捡到的分（不含底牌）。</summary>
    public int DefenderTrickPoints => _defenderTrickPoints;

    /// <summary>被庄家扣掉的分。和闲家的分加起来恒等于 200 减去底牌里的分。</summary>
    public int DeclarerTrickPoints => _declarerTrickPoints;

    public ImmutableArray<int> Levels => [.. _levels];

    public int LevelOf(Team team) => _levels[(int)team];

    public Rank RankOf(Team team) => LevelTrack.RankAt(_levels[(int)team]);

    public IReadOnlyList<Card> HandOf(Seat seat) => _hands[(int)seat];

    /// <summary>底牌。庄家换完之后就是扣下去的那 8 张，打完才翻开。</summary>
    public IReadOnlyList<Card> Bottom => _bottom;

    /// <summary>叫主时摆出来的明牌，这几张不能被扣进底牌。</summary>
    public IReadOnlyList<Card> Locked => _locked;

    // ------------------------------------------------------- 存盘 / 还原（撤回用）

    /// <summary>
    /// 把当前这一局的全部状态拷一份出来。<see cref="RestoreState"/> 能原样放回去。
    ///
    /// 这是「撤回」的地基：撤回点是在**用户出牌之前**抓的，那时候 <see cref="Pending"/>
    /// 正好是递给他的那个 <c>NeedPlay</c>（候选也一起存着）。还原它就等于回到
    /// "轮到你出牌"，不需要再走一遍私有的 <c>Advance()</c>。
    ///
    /// 注意 <c>_rng</c> 存不了（<see cref="Util.IRandomSource"/> 没暴露内部状态），
    /// 所以还原之后 AI 会用新的随机数重决策，不一定按原来那样出牌。
    /// </summary>
    public GameEngineState CaptureState() => new(
        Hands: [.. _hands.Select(hand => hand.ToImmutableArray())],
        Bottom: [.. _bottom],
        Locked: [.. _locked],
        Deck: [.. _deck],
        DealCursor: _dealCursor,
        DealStart: _dealStart,
        SlowDeal: _slowDeal,
        CalledDuringDeal: _calledDuringDeal,
        AcceptedOption: _acceptedOption,
        Levels: [.. _levels],
        LevelsAtRoundStart: [.. _levelsAtRoundStart],
        DeclarerTeam: _declarerTeam,
        HasDeclarer: _hasDeclarer,
        RoundNumber: _roundNumber,
        DeclarerSeat: _declarerSeat,
        Trump: _trump,
        LevelRank: _levelRank,
        CallQueue: [.. _callQueue],
        CallIndex: _callIndex,
        CallRound: _callRound,
        PassedCall: [.. _passedCall],
        Redeals: _redeals,
        HighestCall: _highestCall,
        HighestCaller: _highestCaller,
        CounterQueue: [.. _counterQueue],
        CounterIndex: _counterIndex,
        CounterCount: _counterCount,
        CounterBeforeSwap: _counterBeforeSwap,
        IsFirstRound: _isFirstRound,
        IsGrabRound: _isGrabRound,
        NextRoundIsGrab: _nextRoundIsGrab,
        PerTeamLevel: _perTeamLevel,
        TrickPlays: [.. _trickPlays],
        History: [.. _history],
        Leader: _leader,
        DefenderTrickPoints: _defenderTrickPoints,
        DeclarerTrickPoints: _declarerTrickPoints,
        LastTrickType: _lastTrickType,
        LastTrickWinnerTeam: _lastTrickWinnerTeam,
        TrickNumber: _trickNumber,
        Phase: Phase,
        Pending: Pending,
        RoomWinner: RoomWinner);

    /// <summary>
    /// 把 <see cref="CaptureState"/> 存下来的状态原样放回去。
    ///
    /// 这里能就地改的集合都就地改（<c>_hands</c>/<c>_levels</c>/<c>_trickPlays</c>/<c>_history</c>
    /// 是 readonly 字段，换不掉引用），所以别把它们换成整体替换的写法。
    /// </summary>
    public void RestoreState(GameEngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        for (var i = 0; i < _hands.Length; i++)
        {
            _hands[i].Clear();
            _hands[i].AddRange(state.Hands[i]);
        }

        _bottom = [.. state.Bottom];
        _locked = [.. state.Locked];
        _deck = [.. state.Deck];
        _dealCursor = state.DealCursor;
        _dealStart = state.DealStart;
        _slowDeal = state.SlowDeal;
        _calledDuringDeal = state.CalledDuringDeal;
        _acceptedOption = state.AcceptedOption;

        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i] = state.Levels[i];
            _levelsAtRoundStart[i] = state.LevelsAtRoundStart[i];
        }

        _declarerTeam = state.DeclarerTeam;
        _hasDeclarer = state.HasDeclarer;
        _roundNumber = state.RoundNumber;
        _declarerSeat = state.DeclarerSeat;

        _trump = state.Trump;
        _levelRank = state.LevelRank;

        _callQueue = [.. state.CallQueue];
        _callIndex = state.CallIndex;
        _callRound = state.CallRound;

        _passedCall.Clear();
        _passedCall.UnionWith(state.PassedCall);

        _redeals = state.Redeals;

        _highestCall = state.HighestCall;
        _highestCaller = state.HighestCaller;
        _counterQueue = [.. state.CounterQueue];
        _counterIndex = state.CounterIndex;
        _counterCount = state.CounterCount;
        _counterBeforeSwap = state.CounterBeforeSwap;
        _isFirstRound = state.IsFirstRound;
        _isGrabRound = state.IsGrabRound;
        _nextRoundIsGrab = state.NextRoundIsGrab;
        _perTeamLevel = state.PerTeamLevel;

        _trickPlays.Clear();
        _trickPlays.AddRange(state.TrickPlays);

        _history.Clear();
        _history.AddRange(state.History);

        _leader = state.Leader;
        _defenderTrickPoints = state.DefenderTrickPoints;
        _declarerTrickPoints = state.DeclarerTrickPoints;
        _declarerTrickPoints = state.DeclarerTrickPoints;
        _lastTrickType = state.LastTrickType;
        _lastTrickWinnerTeam = state.LastTrickWinnerTeam;
        _trickNumber = state.TrickNumber;

        Phase = state.Phase;
        Pending = state.Pending;
        RoomWinner = state.RoomWinner;
    }

    // ---------------------------------------------------------------- 对外流程

    /// <summary>
    /// 换一套规则开关。**从下一局开始生效**，正在打的这一局不受影响——
    /// 每局的主牌上下文在叫主的时候就定死了，中途改会前后不一致。
    /// </summary>
    public void UpdateOptions(RuleOptions options) => _options = options;

    /// <summary>开一局。房间结束之后再调也没用，得新建引擎。</summary>
    public IReadOnlyList<GameEvent> StartRound()
    {
        if (Phase == GamePhase.RoomOver)
        {
            throw new InvalidOperationException("房间已经结束了");
        }

        var events = new List<GameEvent>();
        _roundNumber++;
        SetUpRound(events);
        Advance(events);
        return events;
    }

    public IReadOnlyList<GameEvent> Apply(GameCommand command)
    {
        if (Pending is null)
        {
            throw new InvalidOperationException("现在没有待决策的事情，不该提交命令");
        }

        if (command.Seat != Pending.Actor)
        {
            throw new InvalidOperationException(
                $"现在轮到 {Pending.Actor.Name()} 家，不是 {command.Seat.Name()} 家");
        }

        var events = new List<GameEvent>();
        Pending = null;

        switch (command)
        {
            case DeclareCall call:
                HandleDeclareCall(call, events);
                break;

            case PassCall pass:
                events.Add(new CallPassed(pass.Seat));

                // 反主阶段和叫主阶段各有各的队列，别推错了——推错就会一直问同一个人。
                if (Phase == GamePhase.CounterCalling)
                {
                    _counterIndex++;
                }
                else
                {
                    _passedCall.Add(pass.Seat);
                    _callIndex++;
                }

                break;

            case CommitBottom bottom:
                HandleCommitBottom(bottom, events);
                break;

            case PlayCards play:
                HandlePlay(play, events);
                break;

            default:
                throw new NotSupportedException($"未知的命令：{command.GetType().Name}");
        }

        Advance(events);
        return events;
    }

    // ---------------------------------------------------------------- 内部推进

    private void Advance(List<GameEvent> events)
    {
        var guard = 0;

        while (Pending is null && Phase is not (GamePhase.RoundOver or GamePhase.RoomOver))
        {
            if (++guard > 10_000)
            {
                throw new InvalidOperationException("引擎推进不动了，可能哪里没有出口");
            }

            switch (Phase)
            {
                case GamePhase.Dealing:
                    // 发牌由界面的节拍推着走（AdvanceDeal），引擎不自己一口气发完，
                    // 不然"发牌途中抢主"就没有窗口了。
                    return;

                case GamePhase.Calling:
                    AdvanceCalling(events);
                    break;

                case GamePhase.CounterCalling:
                    AdvanceCounterCalling(events);
                    break;

                case GamePhase.BottomSwap:
                    // 换底牌必须等决策，进不来这里。
                    throw new InvalidOperationException("换底牌阶段没有产生待决策");

                case GamePhase.Playing:
                    AdvancePlaying(events);
                    break;

                case GamePhase.Settling:
                    AdvanceSettling(events);
                    break;

                default:
                    return;
            }
        }
    }

    private void SetUpRound(List<GameEvent> events)
    {
        _redeals = 0;
        SetUpDeal(events, announce: true);
    }

    /// <summary>
    /// 洗牌、清局面。
    ///
    /// **首局是"抢先叫主"**：牌一张一张发，谁先凑得出能叫的牌谁就当场叫（文档第 10 行）。
    /// 所以首局不能一次发完，得让 UI 按节拍推着走——这也是慢发牌存在的意义，
    /// 不是给人看动画，是给人留出抢主的窗口。
    /// 其它局直接一次发完再进叫主。
    /// </summary>
    private void SetUpDeal(List<GameEvent> events, bool announce)
    {
        _deck = [.. Deck.CreateShuffled(_rng)];

        // 文档第 21 行：发到最后锅底剩 8 张。
        _bottom = [.. _deck.Skip(TotalDealt).OrderBy(c => c.Id)];

        _dealCursor = 0;
        _dealStart = (Seat)_rng.Next(4);
        _calledDuringDeal = false;
        _acceptedOption = null;
        _highestCall = null;
        _counterQueue = [];
        _counterIndex = 0;
        _counterCount = 0;
        _counterBeforeSwap = false;
        _isFirstRound = !_hasDeclarer;
        _isGrabRound = _nextRoundIsGrab;
        _nextRoundIsGrab = false;
        _perTeamLevel = _isFirstRound || _isGrabRound;
        _locked = [];
        _trump = null;
        _trickPlays.Clear();
        _history.Clear();
        _defenderTrickPoints = 0;
        _declarerTrickPoints = 0;
        _trickNumber = 0;
        _lastTrickType = ComboType.Invalid;

        for (var seat = 0; seat < 4; seat++)
        {
            _hands[seat] = [];
        }

        _levelRank = RankOf(_declarerTeam);
        Array.Copy(_levels, _levelsAtRoundStart, 2);

        if (announce)
        {
            events.Add(new RoundStarted(_roundNumber, [.. _levels], _hasDeclarer ? _declarerTeam : null));
        }

        // 首局和抢主局都走慢发牌 —— 因为"抢先叫主"就发生在发牌这一段
        // （抢主局也是谁先叫谁坐庄，文档第 45、86 行）。
        _slowDeal = _isFirstRound || _isGrabRound;
        Phase = GamePhase.Dealing;

        if (!_slowDeal)
        {
            while (_dealCursor < TotalDealt)
            {
                DealOneRound();
            }

            FinishDealing(events);
        }
    }

    /// <summary>
    /// 发一轮：每人一张，**顺时针**（东 → 南 → 西 → 北）。
    /// 由界面按节拍调用，见 <see cref="AdvanceDeal"/>。
    /// </summary>
    public IReadOnlyList<GameEvent> AdvanceDeal()
    {
        var events = new List<GameEvent>();

        if (Phase != GamePhase.Dealing || Pending is not null)
        {
            return events;
        }

        DealOneRound();
        events.Add(new DealProgress(_dealCursor / 4, CardsPerSeat));

        // 发到一半有人叫得出来了 —— 这就是抢主，停下来让他叫。
        if (TryEarlyCall())
        {
            return events;
        }

        if (_dealCursor >= TotalDealt)
        {
            FinishDealing(events);
            Advance(events);
        }

        return events;
    }

    private void DealOneRound()
    {
        for (var i = 0; i < 4 && _dealCursor < TotalDealt; i++)
        {
            _hands[(int)DealSeatAt(_dealCursor)].Add(_deck[_dealCursor]);
            _dealCursor++;
        }

        foreach (var hand in _hands)
        {
            hand.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
    }

    /// <summary>第 <paramref name="index"/> 张牌发给谁——发牌是顺时针的。</summary>
    private Seat DealSeatAt(int index) => (Seat)(((int)_dealStart + index) % 4);

    /// <summary>
    /// 发牌途中问一圈：谁现在就叫得出来了？
    /// 按发牌顺序问，谁先叫得出来谁坐庄（文档："抢先叫主，谁先叫牌谁庄家"）。
    /// 有牌必须叫，所以这里**不给让牌的余地**。
    /// </summary>
    private bool TryEarlyCall()
    {
        if (_calledDuringDeal)
        {
            return false;
        }

        foreach (var seat in Seats.InDealOrderFrom(_dealStart))
        {
            // 首局还没定庄，各家用**自己队**的等级去叫；叫成了本局级牌就是他的等级。
            var levelRank = RankOf(seat.TeamOf());
            var options = CallFinder.FindAll(_hands[(int)seat], [levelRank], _options);

            if (options.Count == 0)
            {
                continue;
            }

            _levelRank = levelRank;
            _highestCall = options[0];
            _highestCaller = seat;

            Pending = new PendingDecision.NeedCall(
                seat, [.. _hands[(int)seat]], options, CanPass: false, levelRank);
            return true;
        }

        return false;
    }

    private void FinishDealing(List<GameEvent> events)
    {
        if (_calledDuringDeal)
        {
            // 首局没有庄家：先把反主打完，再拿底牌（文档第 44、88 行）。
            if (_isFirstRound && _options.AllowCounterCalls)
            {
                BeginCounterWar(beforeSwap: true);
                return;
            }

            // 其它局（含抢主局）：按"换牌以后才可以反"，先换底牌。
            EnterBottomSwap(_declarerSeat);
            return;
        }

        if (_slowDeal)
        {
            // 首局/抢主局是发牌途中叫主。发到底都没人叫得出来，那就是真的叫不起。
            Redeal(events, "发到最后四家都叫不起");
            return;
        }

        Phase = GamePhase.Calling;
        BeginCalling();
    }

    /// <summary>别家手上所有的牌（不含锅底）。甩牌合不合法要看这个。</summary>
    private Card[] OtherHandCards(Seat except) =>
        [.. Seats.All.Where(seat => seat != except).SelectMany(seat => _hands[(int)seat])];

    /// <summary>
    /// 领出这一手合不合法。除了牌型本身，甩牌还要看这门里别人手上有没有更大的牌
    /// （文档第 27 行：线上就由系统拦住，不做罚则）。
    /// </summary>
    public ValidationResult ValidateLead(Seat seat, CardCombination combo)
    {
        var basic = MoveValidator.ValidateLead(combo);
        if (!basic.IsValid)
        {
            return basic;
        }

        if (combo.Type == ComboType.Throw
            && _trump is not null
            && !ThrowRules.IsLegal(combo, OtherHandCards(seat), _trump))
        {
            return ValidationResult.Fail("这门里别人手上还有更大的牌，甩不出去");
        }

        return ValidationResult.Ok;
    }

    /// <summary>给界面用：玩家选了这几张准备领出，看看行不行。</summary>
    public ValidationResult ValidateLead(Seat seat, IReadOnlyList<Card> cards)
    {
        if (_trump is null)
        {
            return ValidationResult.Fail("还没定主呢");
        }

        return ComboRecognizer.TryRecognize(cards, _trump, out var combo, out var reason)
            ? ValidateLead(seat, combo!)
            : ValidationResult.Fail(reason);
    }

    /// <summary>谁叫的牌（或者谁反成功的），谁拿底牌换牌（文档第 22、44 行）。</summary>
    private void EnterBottomSwap(Seat swapper)
    {
        Phase = GamePhase.BottomSwap;
        Pending = new PendingDecision.NeedBottomSwap(
            swapper, [.. _hands[(int)swapper]], [.. _bottom], [.. _locked]);
    }

    // ---------------------------------------------------------------- 反主

    /// <summary>这一局各家用哪个级牌。首局和抢主局各用各家的，其它局统一用庄家的。</summary>
    private Rank LevelRankFor(Seat seat) =>
        _perTeamLevel ? RankOf(seat.TeamOf()) : RankOf(_declarerTeam);

    private void BeginCounterWar(bool beforeSwap)
    {
        _counterBeforeSwap = beforeSwap;
        Phase = GamePhase.CounterCalling;
        BeginCounterQueue();
    }

    /// <summary>问对手那一队的两个人。队友不能反（文档第 55 行）。</summary>
    private void BeginCounterQueue()
    {
        var opponents = _highestCaller.TeamOf().Other();

        _counterQueue =
        [
            .. Seats.InPlayOrderFrom(_highestCaller.NextInPlay()).Where(seat => seat.TeamOf() == opponents),
        ];

        _counterIndex = 0;
    }

    private void AdvanceCounterCalling(List<GameEvent> events)
    {
        if (_highestCall is null)
        {
            throw new InvalidOperationException("还没人叫主，就进反主阶段了");
        }

        while (_counterIndex < _counterQueue.Count)
        {
            var seat = _counterQueue[_counterIndex];
            var options = FindCounters(seat);

            if (options.Count == 0)
            {
                _counterIndex++;
                continue;
            }

            // 反主永远是想反才反，不存在"必须反"。
            Pending = new PendingDecision.NeedCall(
                seat,
                [.. _hands[(int)seat]],
                options,
                CanPass: true,
                LevelRankFor(seat),
                IsCounter: true);
            return;
        }

        FinishCounterWar(events);
    }

    /// <summary>手上真有的、而且规格比当前更高的反法。同规格不能反。</summary>
    private IReadOnlyList<CallOption> FindCounters(Seat seat)
    {
        var level = LevelRankFor(seat);

        return
        [
            .. CallFinder
                .FindAll(_hands[(int)seat], [level], _options)
                .Where(option => option.Priority > _highestCall!.Priority),
        ];
    }

    private void HandleCounter(DeclareCall command, List<GameEvent> events)
    {
        _highestCall = command.Option;
        _highestCaller = command.Seat;
        _counterCount++;

        // 反主改的是主牌和换底权的归属，当面这几张明牌也跟着换人。
        _trump = BuildTrump(command.Option);
        _locked = [.. command.Option.ShowCards];

        events.Add(new CounterCalled(command.Seat, command.Option));

        if (_counterBeforeSwap)
        {
            // 首局：底牌还没动过，接着问另一队还要不要反（文档第 44 行可以一直反）。
            BeginCounterQueue();
            return;
        }

        // 其它局：反的人拿底牌换牌，换完再问回去。
        EnterBottomSwap(command.Seat);
    }

    private void FinishCounterWar(List<GameEvent> events)
    {
        events.Add(new CounterCallingSettled(_highestCaller, _highestCall!, _counterCount));

        if (!_counterBeforeSwap)
        {
            // 普通局和抢主局：反主**不改庄闲身份、也不改谁先出牌**（文档第 87 行），
            // 直接开打，先手还是最早叫主的那个庄家。
            StartPlaying();
            return;
        }

        // 首局：最后叫/反成功的那家才是庄家，然后才拿底牌。
        var changed = _highestCaller != _declarerSeat;

        _declarerTeam = _highestCaller.TeamOf();
        _declarerSeat = _highestCaller;
        _hasDeclarer = true;
        _levelRank = RankOf(_declarerTeam);
        _locked = [.. _highestCall!.ShowCards];
        _trump = BuildTrump(_highestCall);

        if (changed)
        {
            events.Add(new TrumpEstablished(_highestCall, _highestCaller, _levelRank));
        }

        _bottom = [.. _deck.Skip(TotalDealt).OrderBy(c => c.Id)];
        EnterBottomSwap(_highestCaller);
    }

    private void StartPlaying()
    {
        Phase = GamePhase.Playing;
        _leader = _declarerSeat;
        _trickPlays.Clear();
        _trickNumber = 0;
    }

    private void BeginCalling()
    {
        if (!_hasDeclarer)
        {
            // 文档第 44 行：首局没有庄家，抢先叫主，谁先叫谁是庄。
            _callRound = CallRound.FirstRound;
            _callQueue = [.. Seats.InPlayOrderFrom((Seat)_rng.Next(4))];
        }
        else
        {
            // 文档第 42 行：庄家先叫，有牌必须叫；叫不起才轮到闲家。
            _callRound = CallRound.DeclarerTeam;
            _callQueue = [.. _declarerTeam.Members()];
        }

        _passedCall.Clear();
        _callIndex = 0;
    }

    private void AdvanceCalling(List<GameEvent> events)
    {
        while (_callIndex < _callQueue.Count)
        {
            var seat = _callQueue[_callIndex];
            var options = FindCalls(seat);

            if (options.Count == 0)
            {
                // 叫不起，直接跳过（文档第 51 行：不满足叫牌规则就无法叫牌）。
                _callIndex++;
                continue;
            }

            Pending = new PendingDecision.NeedCall(
                seat, [.. _hands[(int)seat]], options, CanPass(seat), _levelRank);
            return;
        }

        OnCallingExhausted(events);
    }

    /// <summary>
    /// 能不能把叫牌的机会让给队友。文档第 50 行允许"商量谁来叫"，
    /// 但文档第 51 行又说"有牌必须叫"——两者合起来就是：
    /// 只有当队友叫得起、**而且还没让过**的时候才可以让。
    /// 少了后半个条件，两个队友就会互相让到没人叫，那是违规的。
    /// 首局是"抢先叫主"，谁先叫谁庄，不存在让。
    /// </summary>
    private bool CanPass(Seat seat)
    {
        if (_callRound == CallRound.FirstRound)
        {
            return false;
        }

        return !_passedCall.Contains(seat.Partner()) && FindCalls(seat.Partner()).Count > 0;
    }

    private IReadOnlyList<CallOption> FindCalls(Seat seat) =>
        CallFinder.FindAll(_hands[(int)seat], [_levelRank], _options);

    private void OnCallingExhausted(List<GameEvent> events)
    {
        switch (_callRound)
        {
            case CallRound.FirstRound:
                Redeal(events, "四家都叫不起");
                return;

            case CallRound.DeclarerTeam:
                // 庄家叫不起：暂时只记下，等闲家真叫出来了才判负掉级；
                // 万一闲家也叫不起，那是重洗、庄闲不变（文档第 94 行），谁都不掉级。
                _callRound = CallRound.DefenderTeam;
                var defenders = _declarerTeam.Other();
                _callQueue = [.. defenders.Members()];

                // 闲家改用自己家的级牌叫（文档第 93 行：本局级牌按本局庄家算）。
                _levelRank = RankOf(defenders);
                _callIndex = 0;
                return;

            case CallRound.DefenderTeam:
                Redeal(events, "庄家和闲家都叫不起");
                return;

            default:
                throw new InvalidOperationException($"未知的叫牌轮次：{_callRound}");
        }
    }

    private void Redeal(List<GameEvent> events, string reason)
    {
        if (++_redeals > MaxRedeals)
        {
            throw new InvalidOperationException("连续重洗太多次了，规则上不该出现");
        }

        events.Add(new Redeal(reason));
        SetUpDeal(events, announce: false);
    }

    private void HandleDeclareCall(DeclareCall command, List<GameEvent> events)
    {
        if (Phase == GamePhase.CounterCalling)
        {
            HandleCounter(command, events);
            return;
        }

        events.Add(new CallMade(command.Seat, command.Option));
        _acceptedOption = command.Option;
        _highestCall = command.Option;
        _highestCaller = command.Seat;

        if (Phase == GamePhase.Dealing)
        {
            // 发牌途中抢到庄了。牌还得接着发完，发完了才轮到拿底牌换牌。
            _declarerTeam = command.Seat.TeamOf();
            _declarerSeat = command.Seat;
            _hasDeclarer = true;
            _calledDuringDeal = true;
            _levelRank = RankOf(_declarerTeam);
            _locked = [.. command.Option.ShowCards];
            _trump = BuildTrump(command.Option);

            events.Add(new TrumpEstablished(command.Option, command.Seat, _levelRank));

            // 抢主局：抢到的那队保住等级，没抢到的那队掉级（文档第 86 行）。
            if (_isGrabRound)
            {
                var loser = command.Seat.TeamOf().Other();
                var before = _levels[(int)loser];
                var after = LevelTrack.Demote(before);
                _levels[(int)loser] = after;
                events.Add(new GrabRoundSettled(command.Seat.TeamOf(), loser, after != before));
            }

            return;
        }

        if (_callRound == CallRound.DefenderTeam)
        {
            // 庄家无牌叫不起，自动判负：等级清除（文档第 43、94 行）。
            var oldDeclarer = _declarerTeam;
            var before = _levels[(int)oldDeclarer];
            var after = LevelTrack.Demote(before);
            _levels[(int)oldDeclarer] = after;
            events.Add(new DeclarerHandover(oldDeclarer, command.Seat.TeamOf(), after != before));
        }

        _declarerTeam = command.Seat.TeamOf();
        _declarerSeat = command.Seat;
        _hasDeclarer = true;
        _levelRank = RankOf(_declarerTeam);
        _locked = [.. command.Option.ShowCards];
        _trump = BuildTrump(command.Option);

        events.Add(new TrumpEstablished(command.Option, command.Seat, _levelRank));

        EnterBottomSwap(command.Seat);
    }

    private TrumpContext BuildTrump(CallOption option) => option.Kind switch
    {
        CallKind.SuitedSuit or CallKind.SuitedBoth =>
            TrumpContext.Suited(option.TrumpSuit!.Value, _levelRank, option.Kind, _options),
        CallKind.SmallNoTrump => TrumpContext.SmallNoTrump(_levelRank, _options),
        CallKind.BigNoTrump => TrumpContext.BigNoTrump(_levelRank, _options),
        _ => throw new NotSupportedException($"未知的叫主规格：{option.Kind}"),
    };

    private void HandleCommitBottom(CommitBottom command, List<GameEvent> events)
    {
        // 文档第 22 行："叫牌阶段中，叫的牌就把锅底的 8 张牌拿上去，在手上换牌，
        // 根据自己的情况去换牌，返回丢掉 8 张牌进入锅底。"
        // 所以真正能挑的是**手上 25 张 + 锅底 8 张 = 33 张**，不是只从 25 张里挑。
        var pool = _hands[(int)command.Seat].Concat(_bottom).ToArray();

        if (command.Discards.Length != 8)
        {
            throw new InvalidOperationException($"底牌要扣 8 张，你扣了 {command.Discards.Length} 张");
        }

        if (!CardSet.ContainsAll(pool, command.Discards))
        {
            throw new InvalidOperationException("扣的牌不在手上（手上 25 张加锅底 8 张）");
        }

        // 文档第 56 行："可以扣任意牌，包括分，但不能扣你叫主的那几张牌。"
        var lockedId = _locked.Select(c => c.Id).ToHashSet();
        if (command.Discards.Any(c => lockedId.Contains(c.Id)))
        {
            throw new InvalidOperationException("叫主摆出来的明牌不能扣进底牌");
        }

        // 剩下 25 张留在手上，扣出去的 8 张成为新的锅底。
        var kept = CardSet.Remove(pool, command.Discards);
        _hands[(int)command.Seat] = [.. kept.OrderBy(c => c.Id)];
        _bottom = [.. command.Discards.OrderBy(c => c.Id)];

        events.Add(new BottomExchanged(command.Seat, 8));

        // 换完牌，问问对手要不要反主。
        // 首局的反主在拿底牌之前已经打过了；反到大无主（双大王）就没人反得动了。
        if (!_counterBeforeSwap && CanStillCounter())
        {
            BeginCounterWar(beforeSwap: false);
            return;
        }

        StartPlaying();
    }

    /// <summary>当前这个规格还有没有可能被反。双大王是顶，到这儿就定格了。</summary>
    private bool CanStillCounter() =>
        _highestCall is not null
        && _highestCall.Priority < (int)CallKind.BigNoTrump
        && _options.AllowCounterCalls;

    private void AdvancePlaying(List<GameEvent> events)
    {
        if (_trickPlays.Count == 4)
        {
            ResolveTrick(events);
            if (Phase != GamePhase.Playing)
            {
                return;
            }
        }

        var seat = _trickPlays.Count == 0 ? _leader : _trickPlays[^1].Seat.NextInPlay();
        var lead = _trickPlays.Count == 0 ? null : _trickPlays[0].Combo;

        // 领出的时候要把别家的牌递给生成器——甩牌合不合法得看这门里别人手上还有没有更大的。
        var candidates = MoveGenerator.Generate(
            lead,
            [.. _hands[(int)seat]],
            _trump!,
            lead is null ? OtherHandCards(seat) : null);

        Pending = new PendingDecision.NeedPlay(seat, [.. _hands[(int)seat]], lead, candidates);
    }

    private void HandlePlay(PlayCards command, List<GameEvent> events)
    {
        var trump = _trump ?? throw new InvalidOperationException("还没定主呢");
        var hand = _hands[(int)command.Seat];
        var lead = _trickPlays.Count == 0 ? null : _trickPlays[0].Combo;

        CardCombination combo;

        if (lead is null)
        {
            if (!ComboRecognizer.TryRecognize(command.Cards, trump, out var leadCombo, out var reason))
            {
                throw new InvalidOperationException($"这手牌不是合法牌型：{reason}");
            }

            var check = ValidateLead(command.Seat, leadCombo!);
            if (!check.IsValid)
            {
                throw new InvalidOperationException(check.Reason);
            }

            combo = leadCombo!;
        }
        else
        {
            var check = MoveValidator.Validate(lead, hand, command.Cards, trump);
            if (!check.IsValid)
            {
                throw new InvalidOperationException(check.Reason);
            }

            // 凑不成型就是散牌。要是全主牌，它可能能杀散牌领出（见 TrickEvaluator），
            // 所以给它带上最大牌力，比分的时候才有依据。
            combo = ComboRecognizer.TryRecognize(command.Cards, trump, out var followCombo)
                ? followCombo!
                : CardCombination.Shapeless(
                    command.Cards,
                    command.Cards.All(c => IdentityResolver.Resolve(c, trump).IsTrump),
                    lead.Group,
                    command.Cards.Max(c => IdentityResolver.Resolve(c, trump).Order));
        }

        _hands[(int)command.Seat] = [.. CardSet.Remove(hand, command.Cards)];
        _trickPlays.Add(new TrickPlay(command.Seat, combo));
        events.Add(new CardPlayed(command.Seat, combo));
    }

    private void ResolveTrick(List<GameEvent> events)
    {
        var winnerIndex = TrickEvaluator.WinnerIndex([.. _trickPlays.Select(p => p.Combo)]);
        var winner = _trickPlays[winnerIndex];
        var points = _trickPlays.Sum(p => p.Combo.Points);

        var defenderWon = winner.Seat.TeamOf() != _declarerTeam;
        if (defenderWon)
        {
            _defenderTrickPoints += points;
        }
        else
        {
            _declarerTrickPoints += points;
        }

        _lastTrickType = winner.Combo.Type;
        _lastTrickWinnerTeam = winner.Seat.TeamOf();
        _leader = winner.Seat;
        _trickNumber++;

        events.Add(new TrickWon(winner.Seat, points, defenderWon, _trickNumber, [.. _trickPlays]));
        _history.Add(new TrickRecord(_trickNumber, winner.Seat, [.. _trickPlays], points, defenderWon));
        _trickPlays.Clear();

        if (_hands.All(h => h.Count == 0))
        {
            Phase = GamePhase.Settling;
        }
    }

    private void AdvanceSettling(List<GameEvent> events)
    {
        var bottomResult = BottomSettlement.Settle(
            _bottom, _lastTrickType, _lastTrickWinnerTeam != _declarerTeam);
        events.Add(new BottomRevealed(bottomResult, [.. _bottom]));

        var defenderPoints = _defenderTrickPoints + bottomResult.Score;
        var outcome = RoundResultEvaluator.Evaluate(defenderPoints);
        events.Add(new RoundSettled(outcome, defenderPoints));

        // 用开局时的等级做"变之前"——叫主阶段掉级之类的事情才算得进去。
        var before = ImmutableArray.CreateRange(_levelsAtRoundStart);
        var finished = ApplyOutcome(outcome, events);
        events.Add(new LevelsChanged(before, [.. _levels], _declarerTeam));

        if (finished)
        {
            events.Add(new RoomFinished(RoomWinner));
            Phase = GamePhase.RoomOver;
        }
        else
        {
            Phase = GamePhase.RoundOver;
        }
    }

    /// <summary>升级掉级。返回值表示这一局有没有把整个房间打完。</summary>
    private bool ApplyOutcome(RoundOutcome outcome, List<GameEvent> events)
    {
        var declarer = _declarerTeam;
        var defenders = declarer.Other();

        switch (outcome)
        {
            case RoundOutcome.DeclarerUp2:
                return Award(declarer, 2);

            case RoundOutcome.DeclarerUp1:
                return Award(declarer, 1);

            case RoundOutcome.DefenderUp2:
                _declarerTeam = defenders;
                return Award(defenders, 2);

            case RoundOutcome.DefenderUp1:
                _declarerTeam = defenders;
                return Award(defenders, 1);

            case RoundOutcome.DefenderBanQuan:
                // 文档第 86 行：庄家掉级到 5，闲家不升级，只是下一局做庄。
                _levels[(int)declarer] = LevelTrack.Demote(_levels[(int)declarer]);
                _declarerTeam = defenders;
                return false;

            case RoundOutcome.GrabRound:
                // 文档第 45 行：闲家 85~114 分，抢主局 —— 谁都没有赢，等级和庄闲都不变，
                // 但**下一局改成抢先叫主**（双方各用自家的级牌去抢），见 _nextRoundIsGrab。
                _nextRoundIsGrab = true;
                return false;

            case RoundOutcome.DeclarerCannotCall:
                _levels[(int)declarer] = LevelTrack.Demote(_levels[(int)declarer]);
                _declarerTeam = defenders;
                return false;

            default:
                throw new NotSupportedException($"未知的结算结果：{outcome}");
        }
    }

    private bool Award(Team winner, int levels)
    {
        var index = (int)winner;
        var wasAtAce = LevelTrack.IsAce(_levels[index]);

        _levels[index] = LevelTrack.Advance(_levels[index], levels);
        _levels[(int)winner.Other()] = LevelTrack.Demote(_levels[(int)winner.Other()]);

        if (wasAtAce)
        {
            // 文档第 41 行："打到了 A 还赢了，那么就是赢了，这一整局全部推倒了。"
            RoomWinner = winner;
            return true;
        }

        return false;
    }
}
