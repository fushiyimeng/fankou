using System.Drawing.Drawing2D;
using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Controls;

/// <summary>牌桌要显示的东西。控制器在状态变化后填一份给牌桌刷新。</summary>
public sealed record TableSnapshot
{
    public IReadOnlyList<TrickPlay> Trick { get; init; } = [];

    public Seat HumanSeat { get; init; } = Seat.South;

    public string TrumpText { get; init; } = "还没定主";

    public string LevelText { get; init; } = "-";

    public Team DeclarerTeam { get; init; } = Team.EastWest;

    public Seat? DeclarerSeat { get; init; }

    public int DefenderPoints { get; init; }

    public int DeclarerPoints { get; init; }

    public IReadOnlyList<int> RemainingCounts { get; init; } = [0, 0, 0, 0];

    public int TrickNumber { get; init; }

    public IReadOnlyList<Card> Bottom { get; init; } = [];

    public bool BottomRevealed { get; init; }

    /// <summary>叫主时摆出来给大家看的明牌，开打之前一直摊在桌上。</summary>
    public IReadOnlyList<Card> ShownCards { get; init; } = [];

    public Seat? ShownCardsSeat { get; init; }

    public string Status { get; init; } = string.Empty;

    public static TableSnapshot Empty { get; } = new();
}

/// <summary>牌桌：四家出的牌摆在中间信息区四周。整块自绘。</summary>
public sealed class TableSurface : Control
{
    private readonly CardRenderer _renderer;
    private TableSnapshot _snapshot = TableSnapshot.Empty;

    public TableSurface(CardRenderer renderer)
    {
        _renderer = renderer;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = _renderer.Theme.TableOuter;
    }

    public void Update(TableSnapshot snapshot)
    {
        _snapshot = snapshot;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var theme = _renderer.Theme;
        var bounds = ClientRectangle;

        using (var brush = new LinearGradientBrush(bounds, theme.TableInner, theme.TableOuter, 60f))
        {
            g.FillRectangle(brush, bounds);
        }

        var info = ComputeInfoRect();
        DrawInfoPanel(g, info);
        DrawPlayedCards(g, info);
    }

    private Rectangle ComputeInfoRect()
    {
        var width = Math.Min(360, (int)(Width * 0.42));
        var height = Math.Min(150, (int)(Height * 0.30));

        return new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
    }

    private void DrawInfoPanel(Graphics g, Rectangle info)
    {
        var theme = _renderer.Theme;

        using (var path = CardVisual.RoundedRect(info, 14))
        using (var brush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        using (var pen = new Pen(Color.FromArgb(90, theme.TableLine), 1.5f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using var titleFont = new Font("Microsoft YaHei", Math.Max(13f, info.Height * 0.135f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft YaHei", Math.Max(12f, info.Height * 0.115f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var accentBrush = new SolidBrush(theme.Accent);
        using var textBrush = new SolidBrush(theme.TextPrimary);
        using var mutedBrush = new SolidBrush(theme.TextMuted);

        var pad = info.Height * 0.12f;
        var y = info.Y + pad;
        var lineHeight = info.Height * 0.23f;

        var header = string.Join("   ", new[]
        {
            $"主牌 {_snapshot.TrumpText}",
            $"级牌 {_snapshot.LevelText}",
        });

        g.DrawString(header, titleFont, accentBrush, info.X + pad, y);
        y += lineHeight * 1.15f;

        var declarer = _snapshot.DeclarerSeat is { } seat
            ? $"{seat.Name()} 家（{_snapshot.DeclarerTeam.Name()}）"
            : "还没定";

        g.DrawString($"庄家 {declarer}", bodyFont, textBrush, info.X + pad, y);
        y += lineHeight;

        g.DrawString(
            $"闲家 {_snapshot.DefenderPoints} 分  ·  庄家扣 {_snapshot.DeclarerPoints} 分",
            bodyFont,
            textBrush,
            info.X + pad,
            y);
        y += lineHeight;

        var remaining = string.Join("  ", Seats.All.Select(s => $"{s.Name()}{_snapshot.RemainingCounts[(int)s]}"));
        g.DrawString($"第 {_snapshot.TrickNumber} 轮  剩余：{remaining}", bodyFont, mutedBrush, info.X + pad, y);
    }

    private void DrawPlayedCards(Graphics g, Rectangle info)
    {
        var cardHeight = Math.Clamp((int)(Height * 0.16), 44, 96);
        var cardWidth = (int)Math.Round(cardHeight * _renderer.Theme.CardAspect);

        foreach (var play in _snapshot.Trick)
        {
            var area = PlayArea(play.Seat, info, cardWidth, cardHeight);
            DrawPlay(g, play, area, cardWidth, cardHeight);
        }

        // 明牌一直摊到正式开打。叫主的人一旦出了第一张，就收回去（这时那块地方要让给出牌区）。
        if (_snapshot.ShownCards.Count > 0
            && _snapshot.ShownCardsSeat is { } caller
            && !_snapshot.Trick.Any(play => play.Seat == caller))
        {
            var area = PlayArea(caller, info, cardWidth, cardHeight);
            DrawShownCards(g, area, cardWidth, cardHeight);
        }
    }

    /// <summary>
    /// 把叫主摆出来的明牌摊在桌上。文档第 56 行说这几张是明牌、必须摆出来让人看见，
    /// 而且要等到正式开打才收回去。
    /// </summary>
    private void DrawShownCards(Graphics g, Rectangle area, int cardWidth, int cardHeight)
    {
        var cards = _snapshot.ShownCards;
        var step = Math.Max(cardWidth / 3, Math.Min((int)(cardWidth * 0.7), (area.Width - cardWidth) / Math.Max(1, cards.Count - 1)));
        var totalWidth = cardWidth + step * Math.Max(0, cards.Count - 1);
        var x = area.X + (area.Width - totalWidth) / 2;

        foreach (var card in cards)
        {
            g.DrawImage(_renderer.GetCard(card, cardWidth, cardHeight), x, area.Y);
            x += step;
        }

        using var font = new Font("Microsoft YaHei", Math.Max(11f, cardHeight * 0.16f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_renderer.Theme.Accent);
        using var format = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString(
            $"明牌（{_snapshot.ShownCardsSeat!.Value.Name()} 家叫主）",
            font,
            brush,
            new RectangleF(area.X, area.Bottom - 20, area.Width, 20),
            format);
    }

    private Rectangle PlayArea(Seat seat, Rectangle info, int cardWidth, int cardHeight)
    {
        var gap = Math.Max(10, Height / 30);
        var areaWidth = cardWidth * 3 + 8;
        var areaHeight = cardHeight + 24;

        // 把"相对自己"的方位换算成屏幕方位：自己永远在下方，
        // 然后按罗盘习惯排——东在右手边、西在左手边，跟两边的座位名牌保持一致。
        var relative = ((int)seat - (int)_snapshot.HumanSeat + 4) % 4;

        return relative switch
        {
            0 => new Rectangle(info.X + (info.Width - areaWidth) / 2, info.Bottom + gap, areaWidth, areaHeight),
            1 => new Rectangle(info.Left - gap - areaWidth, info.Y + (info.Height - areaHeight) / 2, areaWidth, areaHeight),
            2 => new Rectangle(info.X + (info.Width - areaWidth) / 2, info.Y - gap - areaHeight, areaWidth, areaHeight),
            _ => new Rectangle(info.Right + gap, info.Y + (info.Height - areaHeight) / 2, areaWidth, areaHeight),
        };
    }

    private void DrawPlay(Graphics g, TrickPlay play, Rectangle area, int cardWidth, int cardHeight)
    {
        var cards = play.Combo.Cards;
        var step = Math.Min((int)(cardWidth * 0.7), (area.Width - cardWidth) / Math.Max(1, cards.Length - 1));
        step = Math.Max(step, cardWidth / 3);

        var totalWidth = cardWidth + step * Math.Max(0, cards.Length - 1);
        var x = area.X + (area.Width - totalWidth) / 2;
        var y = area.Y;

        foreach (var card in cards)
        {
            g.DrawImage(_renderer.GetCard(card, cardWidth, cardHeight), x, y);
            x += step;
        }

        using var font = new Font("Microsoft YaHei", Math.Max(11f, cardHeight * 0.16f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_renderer.Theme.TextMuted);
        using var format = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString(
            play.Seat.Name(),
            font,
            brush,
            new RectangleF(area.X, area.Bottom - 20, area.Width, 20),
            format);
    }
}
