using System.Diagnostics;
using FanKou.Ai;
using FanKou.Core.Cards;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Util;
using FanKou.Ui.Controls;
using FanKou.Ui.Dialogs;
using FanKou.Ui.Rendering;

namespace FanKou.App;

public sealed class MainForm : Form
{
    private readonly CardRenderer _renderer;
    private readonly ToolStripMenuItem _jokerTractorMenu;
    private RuleOptions _options;
    private readonly TableSurface _table;
    private readonly HandPanel _hand;
    private readonly Dictionary<Seat, SeatPanel> _seatPanels = [];
    private readonly Label _statusLabel;
    private readonly Label _turnLabel;
    private readonly Button _playButton;
    private readonly Button _hintButton;
    private readonly Button _undoButton;
    private readonly Button _clearButton;
    private readonly MenuStrip _menu;
    private readonly ToolStripMenuItem _skinMenu;
    private readonly ToolStripMenuItem _opponentMenu;

    private GameController? _controller;
    private IReadOnlyList<CardSkin> _skins = [];
    private int _hintCursor;

    public MainForm()
    {
        _options = RuleOptionsLoader.LoadFromBaseDirectory();
        _skins = CardSkin.Discover();
        _renderer = new CardRenderer(_skins[0]);

        Text = "双扣（方扣）";

        // 标题栏那个图标。exe 文件本身的图标由 csproj 的 ApplicationIcon 管，
        // 两者是分开的——只设 ApplicationIcon 的话窗口上还是默认那个小方块。
        Icon = LoadAppIcon();

        ClientSize = new Size(1320, 880);
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _renderer.Theme.TableOuter;
        ForeColor = _renderer.Theme.TextPrimary;
        Font = new Font("Microsoft YaHei", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        KeyPreview = true;

        _menu = new MenuStrip { BackColor = _renderer.Theme.TableOuter, ForeColor = _renderer.Theme.TextPrimary };
        _skinMenu = new ToolStripMenuItem("卡牌样式");
        _opponentMenu = new ToolStripMenuItem("对手强度");
        _jokerTractorMenu = new ToolStripMenuItem("两张大王 + 两张小王 算连对")
        {
            CheckOnClick = true,
            Checked = _options.JokerPairChainIsTractor,
        };
        _jokerTractorMenu.Click += (_, _) => ToggleJokerTractor(_jokerTractorMenu.Checked);
        BuildMenu();

        _table = new TableSurface(_renderer) { Dock = DockStyle.Fill };
        _hand = new HandPanel(_renderer)
        {
            Dock = DockStyle.Fill,
            Height = 190,
            InteractionEnabled = false,
        };
        _hand.SelectionChanged += (_, _) => RefreshButtons();

        foreach (var seat in Seats.All)
        {
            _seatPanels[seat] = new SeatPanel(_renderer, seat) { Dock = DockStyle.Fill };
        }

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = _renderer.Theme.TextMuted,
            Text = "点「开始新的一局」开局",
        };

        _turnLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = 160,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 12, 0),
            Font = new Font("Microsoft YaHei", 16f, FontStyle.Bold, GraphicsUnit.Pixel),
            ForeColor = _renderer.Theme.Accent,
        };

        _playButton = MakeButton("出牌", 110, OnPlayClick);
        _hintButton = MakeButton("提示", 90, OnHintClick);
        _undoButton = MakeButton("撤回", 90, OnUndoClick);
        _clearButton = MakeButton("清空", 90, (_, _) => _hand.ClearSelection());

        BuildLayout();

        KeyDown += OnKeyDown;
        FormClosing += (_, _) => _controller?.Dispose();
    }

    /// <summary>
    /// 把嵌在程序集里的图标取出来。取不到就返回 null、让 WinForms 用默认的——
    /// 图标丢了不该让程序起不来。
    /// </summary>
    private static Icon? LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("FanKou.App.app.ico");
        return stream is null ? null : new Icon(stream);
    }

    /// <summary>开发用：塞一个假局面进去，好把界面渲染成图片检查布局。</summary>
    internal void LoadDemoState(bool withTrick = true)
    {
        _table.Update(DemoState.Snapshot(withTrick));
        _hand.SetHand(CardSorter.ForDisplay(DemoState.Hand, DemoState.Trump));
        _hand.InteractionEnabled = true;
        _hand.Select([DemoState.Hand[5], DemoState.Hand[6]]);
        _turnLabel.Text = "● 你的回合";
        _playButton.Enabled = true;
        _hintButton.Enabled = true;
        _undoButton.Enabled = true;
        _clearButton.Enabled = true;
        _statusLabel.Text = "可以出：♠5 ♠5";
        _statusLabel.ForeColor = Color.FromArgb(190, 255, 210);

        var counts = DemoState.Snapshot().RemainingCounts;

        foreach (var seat in Seats.All)
        {
            _seatPanels[seat].Update(
                $"{seat.TeamOf().Name()}  ·  打 5\n剩 {counts[(int)seat]} 张" + (seat == Seat.West ? "  ·  庄" : string.Empty),
                seat == Seat.West ? Color.FromArgb(180, _renderer.Theme.TableInner) : null);
        }
    }

    private void BuildMenu()
    {
        var gameMenu = new ToolStripMenuItem("游戏");

        var start = new ToolStripMenuItem("开始新的一局", null, (_, _) => StartRound());
        var restart = new ToolStripMenuItem("重新开一盘（等级清零）", null, (_, _) => RestartMatch());
        var quit = new ToolStripMenuItem("退出", null, (_, _) => Close());
        gameMenu.DropDownItems.AddRange([start, restart, new ToolStripSeparator(), quit]);

        _skinMenu.DropDownItems.Add(new ToolStripMenuItem("（还没扫描）"));
        RefreshSkinMenu();

        foreach (var level in new[] { AiLevel.Beginner, AiLevel.Simple, AiLevel.Normal, AiLevel.Hard })
        {
            var item = new ToolStripMenuItem(BrainFactory.Name(level))
            {
                Tag = level,
                Checked = level == _aiLevel,
            };

            item.Click += (_, _) => SetAiLevel(level);
            _opponentMenu.DropDownItems.Add(item);
        }

        // 各地规矩不一样的玩法，做成开关点一下就行，不用改文件重启。
        var ruleMenu = new ToolStripMenuItem("规则开关");
        ruleMenu.DropDownItems.Add(_jokerTractorMenu);
        ruleMenu.DropDownItems.Add(new ToolStripSeparator());
        ruleMenu.DropDownItems.Add(new ToolStripMenuItem(
            "其它开关在这里看…", null, (_, _) => ShowRuleHelp()));

        var logMenu = new ToolStripMenuItem("牌局记录", null, (_, _) => ShowLog());

        var helpMenu = new ToolStripMenuItem("帮助");
        helpMenu.DropDownItems.Add("规则说明", null, (_, _) => ShowRules());
        helpMenu.DropDownItems.Add("怎么加自己的卡牌样式", null, (_, _) => ShowSkinHelp());

        _menu.Items.AddRange([gameMenu, _skinMenu, _opponentMenu, ruleMenu, logMenu, helpMenu]);
        MainMenuStrip = _menu;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(6),
            BackColor = _renderer.Theme.TableOuter,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        root.Controls.Add(_seatPanels[Seat.North], 1, 0);
        root.Controls.Add(_seatPanels[Seat.West], 0, 1);
        root.Controls.Add(_table, 1, 1);
        root.Controls.Add(_seatPanels[Seat.East], 2, 1);
        root.Controls.Add(_hand, 0, 2);
        root.SetColumnSpan(_hand, 3);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

        var leftInfo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        leftInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        leftInfo.Controls.Add(_statusLabel, 0, 0);
        leftInfo.Controls.Add(_turnLabel, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
        };
        buttons.Controls.Add(_playButton);
        buttons.Controls.Add(_hintButton);
        buttons.Controls.Add(_undoButton);
        buttons.Controls.Add(_clearButton);

        bottom.Controls.Add(leftInfo, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        root.Controls.Add(bottom, 0, 3);
        root.SetColumnSpan(bottom, 3);

        // 停靠顺序有讲究：先加 Fill 的 root、后加 Top 的菜单，
        // WinForms 会从后往前停靠，这样菜单占顶部、root 占剩下全部。
        Controls.Add(root);
        Controls.Add(_menu);
    }

    private Button MakeButton(string text, int width, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            BackColor = _renderer.Theme.TableInner,
            ForeColor = _renderer.Theme.TextPrimary,
            Enabled = false,
            Margin = new Padding(6, 0, 0, 0),
        };

        button.FlatAppearance.BorderColor = _renderer.Theme.TableLine;
        button.Click += onClick;
        return button;
    }

    // ------------------------------------------------------------ 对局控制

    private void StartRound()
    {
        _controller ??= CreateController();
        _hintCursor = 0;
        _controller.Start();
    }

    private void RestartMatch()
    {
        _controller?.Dispose();
        _controller = null;
        _renderer.ClearCache();
        StartRound();
    }

    private AiLevel _aiLevel = AiLevel.Normal;

    private void SetAiLevel(AiLevel level)
    {
        _aiLevel = level;

        foreach (var item in _opponentMenu.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item.Tag is AiLevel tagged && tagged == level;
        }

        // 换脑子不用重开一盘，下一步决策就按新的来。
        _controller?.UpdateBrains(CreateBrains(SharedRandomSource.Instance));
        _statusLabel.Text = $"对手强度：{BrainFactory.Name(level)}（马上生效）";
        _statusLabel.ForeColor = _renderer.Theme.Accent;
    }

    private IReadOnlyDictionary<Seat, IPlayerBrain> CreateBrains(IRandomSource rng) =>
        Seats.All.ToDictionary(seat => seat, _ => BrainFactory.Create(_aiLevel, rng));

    private GameController CreateController()
    {
        var rng = SharedRandomSource.Instance;
        var engine = new GameEngine(rng, _options);
        var brains = CreateBrains(rng);

        var controller = new GameController(engine, brains, Seat.South, _options);
        controller.Changed += OnControllerChanged;
        controller.HumanTurnChanged += OnHumanTurnChanged;
        controller.RoundFinished += OnRoundFinished;
        return controller;
    }

    private void OnControllerChanged()
    {
        if (_controller is null)
        {
            return;
        }

        _table.Update(_controller.Snapshot());

        _hand.SetHand(CardSorter.ForDisplay(_controller.HumanHand, _controller.Engine.Trump));
        _hand.InteractionEnabled = _controller.HumanPending is PendingDecision.NeedPlay;

        var snapshot = _controller.Snapshot();

        foreach (var seat in Seats.All)
        {
            var team = seat.TeamOf();
            var isDeclarer = _controller.Engine.HasDeclarer && _controller.Engine.DeclarerTeam == team;
            var text = $"{team.Name()}  ·  打 {_controller.Engine.RankOf(team).Text()}"
                + $"\n剩 {snapshot.RemainingCounts[(int)seat]} 张"
                + (isDeclarer ? "  ·  庄" : string.Empty);

            var accent = _controller.Engine.HasDeclarer && _controller.Engine.DeclarerSeat == seat
                ? Color.FromArgb(180, _renderer.Theme.TableInner)
                : (Color?)null;

            _seatPanels[seat].Update(text, accent);
        }

        _statusLabel.Text = _controller.Status;
        _statusLabel.ForeColor = _renderer.Theme.TextMuted;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        // 「撤回」是随时能按的（AI 思考中、一墩打完的暂停里、已经进了下一墩都行），
        // 所以它的灰亮必须放在下面那个"不是你的回合就全灰掉"的早退**之前**。
        _undoButton.Enabled = _controller?.CanUndo == true;

        if (_controller?.HumanPending is not PendingDecision.NeedPlay)
        {
            _playButton.Enabled = false;
            _hintButton.Enabled = false;
            _clearButton.Enabled = false;
            _turnLabel.Text = string.Empty;
            return;
        }

        _turnLabel.Text = "● 你的回合";
        _clearButton.Enabled = _hand.SelectedCards.Count > 0;
        _hintButton.Enabled = _controller.HumanCandidates.Count > 0;

        var check = _controller.ValidateSelection(_hand.SelectedCards);
        _playButton.Enabled = check.IsValid;

        if (_hand.SelectedCards.Count > 0)
        {
            _statusLabel.Text = check.IsValid ? $"可以出：{Describe(_hand.SelectedCards)}" : check.Reason;
            _statusLabel.ForeColor = check.IsValid
                ? Color.FromArgb(190, 255, 210)
                : _renderer.Theme.TextWarning;
        }
    }

    private void OnHumanTurnChanged()
    {
        // 用 BeginInvoke 排到消息队列，避免在「叫主」的对话框还没关掉时又弹出「换底牌」。
        BeginInvoke(HandleHumanTurn);
    }

    private void HandleHumanTurn()
    {
        if (_controller?.HumanPending is not { } pending)
        {
            return;
        }

        switch (pending)
        {
            case PendingDecision.NeedCall needCall:
            {
                var choice = CallDialog.Choose(
                    this, _renderer, needCall.Options, needCall.CanPass, needCall.Actor, needCall.IsCounter);

                // 关掉对话框又没有让牌权（或者干脆没选），就重新问一次，
                // 不然这一步没人推进，整局就卡住了。
                if (choice is null && !needCall.CanPass)
                {
                    BeginInvoke(HandleHumanTurn);
                    break;
                }

                _controller.SubmitCall(choice);
                break;
            }

            case PendingDecision.NeedBottomSwap swap:
            {
                var discards = BottomSwapDialog.Choose(
                    this, _renderer, swap.Hand, swap.Bottom, swap.Locked);
                if (discards.Count == 8)
                {
                    _controller.SubmitBottomDiscards(discards);
                }
                else
                {
                    // 对话框被叉掉了，再来一次。
                    BeginInvoke(HandleHumanTurn);
                }

                break;
            }

            case PendingDecision.NeedPlay:
                // 出牌不用弹窗，状态栏已经在提示了。
                RefreshButtons();
                break;
        }
    }

    private void OnRoundFinished(RoundSummary summary)
    {
        BeginInvoke(() =>
        {
            RoundResultDialog.Show(this, _renderer, summary);

            if (summary.RoomOver)
            {
                RestartMatch();
            }
            else
            {
                _controller?.StartNextRound();
            }
        });
    }

    private void OnPlayClick(object? sender, EventArgs e)
    {
        if (_controller is null)
        {
            return;
        }

        var selected = _hand.SelectedCards;
        var check = _controller.ValidateSelection(selected);

        if (!check.IsValid)
        {
            _statusLabel.Text = check.Reason;
            _statusLabel.ForeColor = _renderer.Theme.TextDanger;
            return;
        }

        _hand.ClearSelection();
        _controller.SubmitPlay(selected);
    }

    private void OnHintClick(object? sender, EventArgs e)
    {
        if (_controller is null)
        {
            return;
        }

        var candidates = _controller.HumanCandidates;
        if (candidates.Count == 0)
        {
            return;
        }

        // 反复点就在候选里轮着看，不重复给同一手。
        _hintCursor = (_hintCursor + 1) % candidates.Count;
        _hand.Select(candidates[_hintCursor].Cards);
    }

    private void OnUndoClick(object? sender, EventArgs e)
    {
        if (_controller?.Undo() == true)
        {
            // 撤回之后手上又多了刚才打出去的那几张，之前选的牌多半已经不是
            // 现在想出的了，清掉省得误导。
            _hand.ClearSelection();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_controller?.IsDealing == true)
        {
            // 发牌期间不接受操作
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            OnPlayClick(sender, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Space)
        {
            OnHintClick(sender, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e is { KeyCode: Keys.Z, Control: true })
        {
            OnUndoClick(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ 菜单

    private void RefreshSkinMenu()
    {
        _skins = CardSkin.Discover();
        _skinMenu.DropDownItems.Clear();

        foreach (var skin in _skins)
        {
            var item = new ToolStripMenuItem(skin.ToString())
            {
                Checked = ReferenceEquals(skin, _renderer.Skin),
                Tag = skin,
            };

            item.Click += (_, _) => UseSkin(skin);
            _skinMenu.DropDownItems.Add(item);
        }

        _skinMenu.DropDownItems.Add(new ToolStripSeparator());
        _skinMenu.DropDownItems.Add(new ToolStripMenuItem("重新扫描", null, (_, _) => RefreshSkinMenu()));
        _skinMenu.DropDownItems.Add(new ToolStripMenuItem("打开样式文件夹", null, (_, _) => OpenSkinFolder()));
    }

    private void UseSkin(CardSkin skin)
    {
        _renderer.UseSkin(skin);
        RefreshSkinMenu();
        OnControllerChanged();
        _hand.Invalidate();
    }

    private void OpenSkinFolder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "skins");

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"打不开文件夹：{ex.Message}", "双扣");
        }
    }

    /// <summary>
    /// 王对算不算连对。各地规矩不一样（跟麻将 159 的反码是 4 张还是 6 张一个道理），
    /// 所以做成开关，点一下就行，下一局生效，同时写回 rules.json 记住。
    /// </summary>
    private void ToggleJokerTractor(bool enabled)
    {
        _options = _options with { JokerPairChainIsTractor = enabled };
        _controller?.ApplyOptions(_options);
        SaveOptions();

        _statusLabel.Text = enabled
            ? "已改：两张大王 + 两张小王算连对（下一局生效）"
            : "已改：两张大王 + 两张小王不算连对（下一局生效）";
        _statusLabel.ForeColor = _renderer.Theme.Accent;
    }

    private void SaveOptions()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, RuleOptionsLoader.FileName),
                RuleOptions.ToJson(_options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"规则改动没存下来：{ex.Message}", "双扣");
        }
    }

    private void ShowRuleHelp() =>
        MessageBox.Show(
            this,
            """
            各地规矩不一样的地方都做成了开关。

            菜单里能直接点的：
            · 两张大王 + 两张小王 算不算连对

            其余的改 rules.json（就在 exe 旁边），改完重开程序：

            JokerPairChainIsTractor   王对算不算连对
            TractorUsesNaturalOrder   拖拉机里级牌提不提权
            PairFillerPolicy          跟对子/拖拉机时什么时候必须垫主牌
                                    TrumpOnlyWhenVoid（默认：空门才垫）
                                    TrumpOnlyWhenPartial / TrumpOnly / AnyCard
            TractorFollowPolicy       有连对是不是必须出连对
                                    StructurePreserving / PairCountOnly
            AllowNoTrumpCalls         开不开放小无主 / 大无主
            """,
            "规则开关");

    private void ShowLog()
    {
        if (_controller is null || _controller.Log.Count == 0)
        {
            MessageBox.Show(this, "还没有记录", "牌局记录");
            return;
        }

        using var dialog = new Form
        {
            Text = "牌局记录（最新在最上面）",
            ClientSize = new Size(760, 620),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = _renderer.Theme.TableOuter,
            ForeColor = _renderer.Theme.TextPrimary,
        };

        var box = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = _renderer.Theme.TableInner,
            ForeColor = _renderer.Theme.TextPrimary,
            Font = new Font("Microsoft YaHei", 13f, FontStyle.Regular, GraphicsUnit.Pixel),
            IntegralHeight = false,
        };

        // 倒着加：最新的在最上面，一打开就看得见，不用往下拉。
        //
        // 原先是在加完之后设 box.TopIndex = 最后一条 想滚到底，但那行**不生效**——
        // 那一刻对话框还没显示、控件句柄还没建出来，WinForms 会把这个赋值丢掉，
        // 列表就永远停在最顶上（也就是最旧的那条）。现在顺序直接体现在列表里，
        // 不依赖任何显示时机。
        for (var i = _controller.Log.Count - 1; i >= 0; i--)
        {
            box.Items.Add(_controller.Log[i]);
        }

        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }

    // 简略版和详细版都在 RulesDialog 里，先给简略的，点按钮翻到详细。
    private void ShowRules() => RulesDialog.Show(this, _renderer);

    private void ShowSkinHelp() =>
        MessageBox.Show(
            this,
            """
            把自己的牌面图放进 skins 文件夹就能用，不用改程序。

            1. 菜单「卡牌样式 → 打开样式文件夹」
            2. 在里面建一个子文件夹，比如 skins\我的牌
            3. 把牌面图丢进去，文件名随便挑一种写法：

               ♠A.png     黑桃A.png     SA.png     spadeA.png
               黑桃10.png  ♠T.png       ST.png
               大王.png    BigJoker.png
               小王.png    SmallJoker.png
               背面.png    back.png

            4. 回到程序，菜单「卡牌样式 → 重新扫描」，选中它即可

            缺哪张牌就用程序自带的画法顶上，所以你可以先只做几张试试。
            放一个 skin.json 可以自定义显示名称：
            { "name": "我的牌", "author": "某人" }
            """,
            "怎么做自己的卡牌样式");

    private static string Describe(IReadOnlyList<Card> cards) =>
        string.Join(" ", cards.Select(CardText.Format));
}
