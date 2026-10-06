using System.Drawing.Drawing2D;
using FanKou.Core.Cards;

namespace FanKou.Ui.Rendering;

/// <summary>
/// 自带的牌面画法。外部样式缺哪张牌，就用这个顶上。
/// </summary>
public static class CardVisual
{
    public static Bitmap RenderFace(Card card, int width, int height, Theme theme)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, width - 1, height - 1);
        var radius = theme.CornerRadius(height);

        using (var path = RoundedRect(bounds, radius))
        using (var brush = new SolidBrush(theme.CardFace))
        using (var pen = new Pen(theme.CardBorder, Math.Max(1f, height / 110f)))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        if (card.IsJoker)
        {
            DrawJoker(g, card, bounds, theme);
        }
        else
        {
            DrawNumberCard(g, card, bounds, theme);
        }

        // 两副牌里第二张同名牌，右上角点一个小点，方便区分（定主要用两张同花色级牌）。
        if (card.Copy == 1)
        {
            var dotSize = Math.Max(3f, height * 0.045f);
            using var dot = new SolidBrush(Color.FromArgb(150, theme.CardBorder));
            g.FillEllipse(dot, width - dotSize * 2.2f, dotSize * 1.1f, dotSize, dotSize);
        }

        return bitmap;
    }

    public static Bitmap RenderBack(int width, int height, Theme theme)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, width - 1, height - 1);
        var radius = theme.CornerRadius(height);

        using (var path = RoundedRect(bounds, radius))
        using (var brush = new LinearGradientBrush(bounds, theme.BackBase, Darken(theme.BackBase), 55f))
        using (var pen = new Pen(theme.BackBorder, Math.Max(1f, height / 110f)))
        {
            g.FillPath(brush, path);
            g.SetClip(path);

            // 斜纹
            using var stripe = new Pen(Color.FromArgb(38, theme.BackAccent), Math.Max(1f, height / 60f));
            var step = Math.Max(5, (int)(theme.BackStripeStep * height / 90f));
            for (var x = -height; x < width + height; x += step * 2)
            {
                g.DrawLine(stripe, x, height, x + height, 0);
            }

            // 中间一块菱形
            var inset = Math.Min(width, height) * 0.22f;
            var diamond = new[]
            {
                new PointF(width / 2f, inset),
                new PointF(width - inset, height / 2f),
                new PointF(width / 2f, height - inset),
                new PointF(inset, height / 2f),
            };

            using (var inner = new SolidBrush(Color.FromArgb(70, theme.BackAccent)))
            {
                g.FillPolygon(inner, diamond);
            }

            using (var outline = new Pen(Color.FromArgb(120, theme.BackAccent), Math.Max(1f, height / 80f)))
            {
                g.DrawPolygon(outline, diamond);
            }

            g.ResetClip();
            g.DrawPath(pen, path);
        }

        return bitmap;
    }

    private static void DrawNumberCard(Graphics g, Card card, Rectangle bounds, Theme theme)
    {
        var color = card.Suit.IsRed() ? theme.RedSuit : theme.BlackSuit;
        var text = card.Rank.Text();

        var rankSize = bounds.Height * 0.235f;
        using var font = new Font("Segoe UI", rankSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);

        var pad = bounds.Height * 0.045f;
        var corner = new RectangleF(pad, pad * 0.7f, bounds.Width - pad * 2, rankSize * 1.35f);

        var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
        };

        g.DrawString(text, font, brush, corner, format);

        // 右下角倒过来的那一份
        var state = g.Save();
        g.TranslateTransform(bounds.Width, bounds.Height);
        g.RotateTransform(180);
        g.DrawString(text, font, brush, corner, format);
        g.Restore(state);

        // 中间的花色
        var suitBox = bounds.Height * 0.46f;
        var suitRect = new RectangleF(
            (bounds.Width - suitBox * 0.9f) / 2f,
            (bounds.Height - suitBox) / 2f + bounds.Height * 0.02f,
            suitBox * 0.9f,
            suitBox);

        SuitArt.Draw(g, card.Suit, suitRect, color);
    }

    private static void DrawJoker(Graphics g, Card card, Rectangle bounds, Theme theme)
    {
        var isBig = card.Rank == Rank.BigJoker;
        var color = isBig ? theme.RedSuit : theme.BlackSuit;

        var size = bounds.Height * 0.20f;
        using var font = new Font("Microsoft YaHei", size, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);

        // 竖着写"小王"/"大王"，省地方也更像牌。
        // 先把字量出来再按中心对齐，比依赖 DrawString 的对齐方式再叠加旋转可靠得多。
        var text = isBig ? "大王" : "小王";
        var textSize = g.MeasureString(text, font);

        var state = g.Save();
        g.TranslateTransform(bounds.Width / 2f, bounds.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString(text, font, brush, -textSize.Width / 2f, -textSize.Height / 2f);
        g.Restore(state);

        // 四角小星
        var star = bounds.Height * 0.09f;
        using var starBrush = new SolidBrush(Color.FromArgb(90, color));
        g.FillEllipse(starBrush, bounds.Width * 0.16f, bounds.Height * 0.14f, star, star);
        g.FillEllipse(starBrush, bounds.Width - bounds.Width * 0.16f - star, bounds.Height * 0.14f, star, star);
        g.FillEllipse(starBrush, bounds.Width * 0.16f, bounds.Height - bounds.Height * 0.14f - star, star, star);
        g.FillEllipse(
            starBrush,
            bounds.Width - bounds.Width * 0.16f - star,
            bounds.Height - bounds.Height * 0.14f - star,
            star,
            star);
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2f;

        if (d <= 0 || bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Darken(Color color) =>
        Color.FromArgb(color.A, (int)(color.R * 0.72), (int)(color.G * 0.72), (int)(color.B * 0.72));
}
