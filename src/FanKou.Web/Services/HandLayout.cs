using FanKou.Core.Cards;

namespace FanKou.Web.Services;

/// <summary>
/// 手牌区的排版参数。跟桌面版 <c>FanKou.Ui.Controls.HandPanel.ComputeLayout</c> 是同一套公式，
/// 只是这里只算"牌多大、抬多少"，每张牌的落点由 <see cref="Layout"/> 按实测宽度算。
/// </summary>
public readonly record struct HandMetrics(int Rows, int PerRow, int RowGap, int CardHeight, int CardWidth, int Raise);

/// <summary>一张牌摆在手牌区里的位置（像素）。</summary>
public readonly record struct HandSlot(Card Card, int X, int Y, int Width, int Height, int Z);

/// <summary>
/// 手牌排版。桌面版是 GDI+ 自己画，位置全在 C# 里算；网页版照搬这套算法
/// （容器宽度靠 JS 的 ResizeObserver 量回来），这样两个版本的间距、抬升、
/// "顶部预留浮起空间"的行为才是一致的，不会各自漂。
/// </summary>
public static class HandLayout
{
    /// <summary>素材自己的比例（viewBox 1500×2100）。桌面版牌框是 0.70，差 1.4%。</summary>
    public const double CardAspect = 1500.0 / 2100.0;

    /// <summary>各档行数对应的控件高度（桌面版主界面 1 行 = 196，换底对话框 2 行 = 330）。</summary>
    public static int HeightForRows(int rows) => rows switch
    {
        <= 1 => 196,
        2 => 330,
        3 => 430,
        _ => 520,
    };

    public static HandMetrics Compute(int height, int rows, int count)
    {
        rows = Math.Clamp(rows, 1, 4);
        count = Math.Max(1, count);

        var perRow = Math.Max(1, (int)Math.Ceiling(count / (double)rows));
        var rowGap = rows > 1 ? 20 : 0;

        // 算两遍：第一遍拿到"抬起来多少"，把这截高度从可用空间里扣掉再算真正的牌高。
        // 这一步就是"顶部预留浮起空间"的全部代价——牌会矮一点。
        var usable0 = Math.Max(40, height - 14 - (rowGap * (rows - 1)));
        var cardH0 = Math.Max(28, (int)Math.Floor(usable0 / (double)rows));
        var raise0 = Math.Max(8, (int)Math.Floor(cardH0 / 7.0));

        var usable1 = Math.Max(40, usable0 - raise0);
        var cardH = Math.Max(28, (int)Math.Floor(usable1 / (double)rows));
        var raise = Math.Max(8, (int)Math.Floor(cardH / 7.0));
        var cardW = Math.Max(20, (int)Math.Round(cardH * CardAspect));

        return new HandMetrics(rows, perRow, rowGap, cardH, cardW, raise);
    }

    public static List<HandSlot> Layout(IReadOnlyList<Card> cards, int width, HandMetrics m)
    {
        var result = new List<HandSlot>(cards.Count);

        if (cards.Count == 0)
        {
            return result;
        }

        var avail = Math.Max(60, width - 16);
        var step = m.PerRow <= 1
            ? m.CardWidth
            : Math.Min(
                (int)Math.Floor(m.CardWidth * 0.62),
                Math.Max(8, (int)Math.Floor((avail - m.CardWidth) / (double)(m.PerRow - 1))));

        var index = 0;
        for (var row = 0; row < m.Rows && index < cards.Count; row++)
        {
            var count = Math.Min(m.PerRow, cards.Count - index);
            var total = m.CardWidth + (step * (count - 1));
            var x0 = Math.Max(8, (int)Math.Floor((width - total) / 2.0));
            var y = m.Raise + 4 + (row * (m.CardHeight + m.RowGap));

            for (var i = 0; i < count; i++, index++)
            {
                result.Add(new HandSlot(cards[index], x0 + (step * i), y, m.CardWidth, m.CardHeight, index));
            }
        }

        return result;
    }
}
