using System.Drawing.Drawing2D;
using FanKou.Core.Cards;

namespace FanKou.Ui.Rendering;

/// <summary>
/// 用路径画四种花色，不依赖字体里有没有 ♠♥♣♦ 这几个字形——
/// 换台机器少个字体就画不出来这种事，不值得冒。
/// 所有坐标都在 0~1 的单位方格里，用的时候缩放到目标矩形。
/// </summary>
public static class SuitArt
{
    public static void Draw(Graphics g, Suit suit, RectangleF box, Color color)
    {
        using var path = Build(suit);
        using var brush = new SolidBrush(color);

        var state = g.Save();
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width, box.Height);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
        g.Restore(state);
    }

    public static GraphicsPath Build(Suit suit) => suit switch
    {
        Suit.Spade => Spade(),
        Suit.Heart => Heart(),
        Suit.Club => Club(),
        Suit.Diamond => Diamond(),
        _ => Diamond(),
    };

    private static GraphicsPath Spade()
    {
        var p = new GraphicsPath();
        p.AddBezier(0.50f, 0.02f, 0.50f, 0.02f, 0.04f, 0.38f, 0.04f, 0.58f);
        p.AddBezier(0.04f, 0.58f, 0.04f, 0.76f, 0.19f, 0.83f, 0.31f, 0.83f);
        p.AddBezier(0.31f, 0.83f, 0.39f, 0.83f, 0.45f, 0.79f, 0.48f, 0.74f);
        p.AddBezier(0.48f, 0.74f, 0.47f, 0.84f, 0.42f, 0.91f, 0.34f, 0.98f);
        p.AddLine(0.34f, 0.98f, 0.66f, 0.98f);
        p.AddBezier(0.66f, 0.98f, 0.58f, 0.91f, 0.53f, 0.84f, 0.52f, 0.74f);
        p.AddBezier(0.52f, 0.74f, 0.55f, 0.79f, 0.61f, 0.83f, 0.69f, 0.83f);
        p.AddBezier(0.69f, 0.83f, 0.81f, 0.83f, 0.96f, 0.76f, 0.96f, 0.58f);
        p.AddBezier(0.96f, 0.58f, 0.96f, 0.38f, 0.50f, 0.02f, 0.50f, 0.02f);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath Heart()
    {
        var p = new GraphicsPath();
        p.AddBezier(0.50f, 0.97f, 0.50f, 0.97f, 0.04f, 0.62f, 0.04f, 0.34f);
        p.AddBezier(0.04f, 0.34f, 0.04f, 0.12f, 0.25f, 0.02f, 0.39f, 0.09f);
        p.AddBezier(0.39f, 0.09f, 0.45f, 0.12f, 0.49f, 0.18f, 0.50f, 0.24f);
        p.AddBezier(0.50f, 0.24f, 0.51f, 0.18f, 0.55f, 0.12f, 0.61f, 0.09f);
        p.AddBezier(0.61f, 0.09f, 0.75f, 0.02f, 0.96f, 0.12f, 0.96f, 0.34f);
        p.AddBezier(0.96f, 0.34f, 0.96f, 0.62f, 0.50f, 0.97f, 0.50f, 0.97f);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath Club()
    {
        var p = new GraphicsPath();

        // 上、左下、右下三个圆瓣
        p.AddEllipse(0.30f, 0.02f, 0.40f, 0.40f);
        p.AddEllipse(0.02f, 0.34f, 0.40f, 0.40f);
        p.AddEllipse(0.58f, 0.34f, 0.40f, 0.40f);

        // 下面那根梗
        p.AddPolygon(new[]
        {
            new PointF(0.43f, 0.62f),
            new PointF(0.57f, 0.62f),
            new PointF(0.63f, 0.98f),
            new PointF(0.37f, 0.98f),
        });

        return p;
    }

    private static GraphicsPath Diamond()
    {
        var p = new GraphicsPath();
        p.AddPolygon(new[]
        {
            new PointF(0.50f, 0.02f),
            new PointF(0.96f, 0.50f),
            new PointF(0.50f, 0.98f),
            new PointF(0.04f, 0.50f),
        });

        return p;
    }
}
