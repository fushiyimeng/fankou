using System.Drawing.Drawing2D;
using FanKou.Core.Cards;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Dialogs;

public sealed record RoundSummary
{
    public int RoundNumber { get; init; }

    public string OutcomeText { get; init; } = string.Empty;

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public IReadOnlyList<Card> Bottom { get; init; } = [];

    public string BottomText { get; init; } = string.Empty;

    public string LevelBefore { get; init; } = string.Empty;

    public string LevelAfter { get; init; } = string.Empty;

    public string NextRoundText { get; init; } = string.Empty;

    public bool RoomOver { get; init; }
}

/// <summary>一局打完的结算：谁赢、底牌翻开是什么、等级怎么变。</summary>
public sealed class RoundResultDialog : Form
{
    private readonly RoundSummary _summary;
    private readonly CardRenderer _renderer;
    private readonly Panel _bottomStrip;

    private RoundResultDialog(CardRenderer renderer, RoundSummary summary)
    {
        _renderer = renderer;
        _summary = summary;

        Text = summary.RoomOver ? "打完了！" : $"第 {summary.RoundNumber} 局结算";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(720, 420);
        BackColor = renderer.Theme.TableOuter;
        ForeColor = renderer.Theme.TextPrimary;

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(16, 14, 0, 0),
            Font = new Font("Microsoft YaHei", 20f, FontStyle.Bold, GraphicsUnit.Pixel),
            Text = summary.OutcomeText,
            ForeColor = renderer.Theme.Accent,
        };

        var details = new Label
        {
            Dock = DockStyle.Top,
            Height = 128,
            Padding = new Padding(16, 4, 16, 0),
            Font = new Font("Microsoft YaHei", 14f, FontStyle.Regular, GraphicsUnit.Pixel),
            Text = string.Join(
                Environment.NewLine,
                $"闲家捡到 {summary.DefenderPoints} 分，庄家扣掉 {summary.DeclarerPoints} 分",
                summary.BottomText,
                $"等级：{summary.LevelBefore}  →  {summary.LevelAfter}",
                string.Empty,
                summary.NextRoundText),
            ForeColor = renderer.Theme.TextPrimary,
        };

        _bottomStrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(16, 0, 0, 0),
        };
        _bottomStrip.Paint += OnPaintBottom;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(10),
        };

        var ok = new Button
        {
            Text = summary.RoomOver ? "重开一局" : "下一局",
            Width = 130,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
        };
        ok.Click += (_, _) => Close();

        buttons.Controls.Add(ok);

        Controls.Add(_bottomStrip);
        Controls.Add(details);
        Controls.Add(title);
        Controls.Add(buttons);
    }

    public static void Show(IWin32Window owner, CardRenderer renderer, RoundSummary summary)
    {
        using var dialog = new RoundResultDialog(renderer, summary);
        dialog.ShowDialog(owner);
    }

    private void OnPaintBottom(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var font = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_renderer.Theme.TextMuted);

        if (_summary.Bottom.Count == 0)
        {
            return;
        }

        g.DrawString("底牌：", font, brush, 0, _bottomStrip.Height / 2f - 10);

        var cardHeight = _bottomStrip.Height - 26;
        var cardWidth = (int)Math.Round(cardHeight * _renderer.Theme.CardAspect);
        var x = 56;

        foreach (var card in _summary.Bottom)
        {
            g.DrawImage(_renderer.GetCard(card, cardWidth, cardHeight), x, 8);
            x += cardWidth + 6;
        }
    }
}
