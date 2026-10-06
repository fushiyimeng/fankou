using FanKou.Core.Game;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Controls;

/// <summary>一个座位的名牌：谁、哪一队、还剩几张牌、是不是庄。</summary>
public sealed class SeatPanel : Panel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly CardRenderer _renderer;

    public SeatPanel(CardRenderer renderer, Seat seat)
    {
        _renderer = renderer;
        Seat = seat;

        BackColor = Color.FromArgb(120, 0, 0, 0);
        Padding = new Padding(10, 8, 10, 8);
        Margin = new Padding(6);

        _title = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Microsoft YaHei", 15f, FontStyle.Bold, GraphicsUnit.Pixel),
            ForeColor = renderer.Theme.TextPrimary,
            Text = $"{seat.Name()} 家",
        };

        _subtitle = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel),
            ForeColor = renderer.Theme.TextMuted,
        };

        Controls.Add(_subtitle);
        Controls.Add(_title);
    }

    public Seat Seat { get; }

    public void Update(string subtitle, Color? accent = null)
    {
        _subtitle.Text = subtitle;
        BackColor = accent is { } color
            ? Color.FromArgb(170, color)
            : Color.FromArgb(120, 0, 0, 0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
