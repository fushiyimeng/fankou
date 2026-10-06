using FanKou.Core.Bidding;
using FanKou.Core.Game;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Dialogs;

/// <summary>
/// 叫主。列出所有叫得出来的规格，并把要摆出来的明牌画在旁边——
/// 文档第 56 行说叫主的牌是明牌，得让人看清楚你用什么叫的。
/// </summary>
public sealed class CallDialog : Form
{
    private readonly CardRenderer _renderer;
    private readonly List<CallOption> _options;
    private readonly ListBox _list;

    private CallDialog(
        CardRenderer renderer,
        IReadOnlyList<CallOption> options,
        bool canPass,
        Seat seat,
        bool isCounter)
    {
        _renderer = renderer;
        _options = [.. options];
        _isCounter = isCounter;

        Text = isCounter ? $"{seat.Name()} 家反主" : $"{seat.Name()} 家叫主";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        // 有牌必须叫的时候不给关，否则关掉就没法继续了。
        ControlBox = canPass;
        ClientSize = new Size(560, 380);
        BackColor = renderer.Theme.TableOuter;
        ForeColor = renderer.Theme.TextPrimary;
        Font = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 76,
            BorderStyle = BorderStyle.None,
            BackColor = renderer.Theme.TableInner,
            ForeColor = renderer.Theme.TextPrimary,
            IntegralHeight = false,
        };

        _list.DrawItem += OnDrawItem;
        _list.DoubleClick += (_, _) => Accept();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(8),
            BackColor = renderer.Theme.TableOuter,
        };

        var confirm = new Button
        {
            Text = isCounter ? "就反这个" : "就这样叫",
            Width = 110,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
        };
        confirm.Click += (_, _) => Accept();

        buttons.Controls.Add(confirm);

        if (canPass)
        {
            var pass = new Button
            {
                Text = isCounter ? "不反" : "让队友叫",
                Width = 110,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
            };
            pass.Click += (_, _) =>
            {
                Result = null;
                DialogResult = DialogResult.OK;
            };

            buttons.Controls.Add(pass);
        }

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(10, 8, 0, 0),
            Text = isCounter
                ? "对手已经叫了。你可以用**更高的规格**把主牌抢过来，不反也行"
                : canPass
                    ? "你看得出来怎么叫，也可以让队友来叫（有牌必须叫，队友叫得起才让得出去）"
                    : "有牌必须叫，选一个吧",
            ForeColor = renderer.Theme.TextMuted,
        };

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);

        foreach (var option in _options)
        {
            _list.Items.Add(option);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = _options.Count - 1;   // 默认选规格最高的那个
        }
    }

    private readonly bool _isCounter;

    /// <summary>返回 null 表示不叫 / 不反。</summary>
    public CallOption? Result { get; private set; }

    public static CallOption? Choose(
        IWin32Window owner,
        CardRenderer renderer,
        IReadOnlyList<CallOption> options,
        bool canPass,
        Seat seat,
        bool isCounter = false)
    {
        if (options.Count == 0)
        {
            return null;
        }

        using var dialog = new CallDialog(renderer, options, canPass, seat, isCounter);
        dialog.ShowDialog(owner);
        return dialog.Result;
    }

    private void Accept()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        Result = _options[_list.SelectedIndex];
        DialogResult = DialogResult.OK;
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _options.Count)
        {
            return;
        }

        var option = _options[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var theme = _renderer.Theme;

        using var background = new SolidBrush(selected ? theme.TableLine : theme.TableInner);
        e.Graphics.FillRectangle(background, e.Bounds);

        using var titleFont = new Font("Microsoft YaHei", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(theme.Accent);
        using var bodyBrush = new SolidBrush(theme.TextMuted);

        e.Graphics.DrawString(option.Describe(), titleFont, titleBrush, e.Bounds.X + 10, e.Bounds.Y + 8);
        e.Graphics.DrawString(
            Rank(option),
            bodyFont,
            bodyBrush,
            e.Bounds.X + 10,
            e.Bounds.Y + 32);

        // 明牌
        var cardHeight = e.Bounds.Height - 18;
        var cardWidth = (int)Math.Round(cardHeight * theme.CardAspect);
        var x = e.Bounds.Right - 12 - cardWidth * option.ShowCards.Length - 6 * (option.ShowCards.Length - 1);
        var y = e.Bounds.Y + 9;

        foreach (var card in option.ShowCards)
        {
            e.Graphics.DrawImage(_renderer.GetCard(card, cardWidth, cardHeight), x, y);
            x += cardWidth + 6;
        }
    }

    private static string Rank(CallOption option) => option.Kind switch
    {
        Core.Trump.CallKind.SuitedSuit => "规格：叫主（最低）",
        Core.Trump.CallKind.SuitedBoth => "规格：定主（比叫主大）",
        Core.Trump.CallKind.SmallNoTrump => "规格：小无主",
        Core.Trump.CallKind.BigNoTrump => "规格：大无主（最高）",
        _ => string.Empty,
    };
}
