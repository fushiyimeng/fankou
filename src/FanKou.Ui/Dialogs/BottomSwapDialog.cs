using FanKou.Core.Cards;
using FanKou.Ui.Controls;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Dialogs;

/// <summary>
/// 换底牌。文档第 22 行："叫的牌就把锅底的 8 张牌拿上去，在手上换牌，
/// 根据自己的情况去换牌，返回丢掉 8 张牌进入锅底。"
///
/// 所以这里先把锅底那 8 张单独摊出来给你看，再合进手牌里一起挑——
/// 挑的范围是 33 张，不是原来的 25 张。
/// 叫主摆出来的明牌不能扣（文档第 56 行），用金色的锁标出来。
/// </summary>
public sealed class BottomSwapDialog : Form
{
    private readonly HandPanel _hand;
    private readonly Label _counter;
    private readonly Button _confirm;
    private readonly IReadOnlyList<Card> _bottom;

    private BottomSwapDialog(
        CardRenderer renderer,
        IReadOnlyList<Card> hand,
        IReadOnlyList<Card> bottom,
        IReadOnlyList<Card> locked)
    {
        _bottom = bottom;

        Text = "换底牌 —— 先看锅底，再从 33 张里挑 8 张扣回去";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(1180, 560);
        BackColor = renderer.Theme.TableOuter;
        ForeColor = renderer.Theme.TextPrimary;
        Font = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

        var bottomStrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 140,
            Padding = new Padding(14, 0, 0, 0),
        };
        bottomStrip.Paint += (_, e) => PaintBottomStrip(e.Graphics, renderer);

        _counter = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(14, 8, 0, 0),
            ForeColor = renderer.Theme.TextMuted,
        };

        // 手上 25 张 + 锅底 8 张，一起摆出来挑；锅底那几张用蓝边标出来。
        var pool = hand.Concat(bottom).ToArray();

        _hand = new HandPanel(renderer)
        {
            Dock = DockStyle.Fill,
            InteractionEnabled = true,
            Rows = 2,
            LockedCards = locked,
            MarkedCards = bottom,
        };
        _hand.SetHand(CardSorter.ForDisplay(pool, null));
        _hand.SelectionChanged += (_, _) => RefreshCounter();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(10),
        };

        _confirm = new Button
        {
            Text = "扣这 8 张",
            Width = 130,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
        };
        _confirm.Click += (_, _) => Close();

        var clear = new Button
        {
            Text = "重选",
            Width = 90,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
        };
        clear.Click += (_, _) => _hand.ClearSelection();

        buttons.Controls.Add(_confirm);
        buttons.Controls.Add(clear);

        Controls.Add(_hand);
        Controls.Add(_counter);
        Controls.Add(bottomStrip);
        Controls.Add(buttons);

        RefreshCounter();
    }

    public IReadOnlyList<Card> Discards { get; private set; } = [];

    /// <summary>
    /// 开发用：造一个对话框出来渲染成图片检查排版，正式流程走 <see cref="Choose"/>。
    /// <paramref name="selectFirst"/> 预先选中开头几张，用来检查牌浮起来会不会被切掉。
    /// </summary>
    public static BottomSwapDialog CreateForPreview(
        CardRenderer renderer,
        IReadOnlyList<Card> hand,
        IReadOnlyList<Card> bottom,
        IReadOnlyList<Card> locked,
        int selectFirst = 0)
    {
        var dialog = new BottomSwapDialog(renderer, hand, bottom, locked);

        if (selectFirst > 0)
        {
            dialog._hand.Select(dialog._hand.Hand.Take(selectFirst));
        }

        return dialog;
    }

    /// <summary>返回扣下去的那 8 张。没选够 8 张就返回空。</summary>
    public static IReadOnlyList<Card> Choose(
        IWin32Window owner,
        CardRenderer renderer,
        IReadOnlyList<Card> hand,
        IReadOnlyList<Card> bottom,
        IReadOnlyList<Card> locked)
    {
        using var dialog = new BottomSwapDialog(renderer, hand, bottom, locked);
        dialog.ShowDialog(owner);
        return dialog.Discards;
    }

    private void PaintBottomStrip(Graphics g, CardRenderer renderer)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var font = new Font("Microsoft YaHei", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var hintFont = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(renderer.Theme.MarkedGlow);
        using var muted = new SolidBrush(renderer.Theme.TextMuted);

        g.DrawString($"锅底这 {_bottom.Count} 张（已经合进下面的手牌里一起挑，蓝边的就是它们）", font, brush, 0, 4);
        g.DrawString(
            "带金色锁的是叫主的明牌，不能扣",
            hintFont,
            muted,
            0,
            26);

        var cardHeight = 92;
        var cardWidth = (int)Math.Round(cardHeight * renderer.Theme.CardAspect);
        var x = 0;
        var y = 46;

        foreach (var card in _bottom)
        {
            g.DrawImage(renderer.GetCard(card, cardWidth, cardHeight), x, y);

            using var pen = new Pen(renderer.Theme.MarkedGlow, 2f);
            using var path = CardVisual.RoundedRect(
                new Rectangle(x + 1, y + 1, cardWidth - 2, cardHeight - 2),
                renderer.Theme.CornerRadius(cardHeight));
            g.DrawPath(pen, path);

            x += cardWidth + 6;
        }
    }

    private void RefreshCounter()
    {
        var count = _hand.SelectedCards.Count;

        _counter.Text = count == 8
            ? "已经选够 8 张，可以扣了"
            : $"已选 {count} / 8 张。（可挑范围：手上 25 张 + 锅底 8 张）";

        _counter.ForeColor = count == 8
            ? Color.FromArgb(180, 255, 200)
            : Color.FromArgb(255, 196, 120);

        _confirm.Enabled = count == 8;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Discards = _hand.SelectedCards;
        base.OnFormClosing(e);
    }
}
