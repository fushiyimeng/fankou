using System.Drawing.Drawing2D;
using FanKou.Core.Cards;

namespace FanKou.Ui.Rendering;

/// <summary>
/// 把各种牌画成一张对照图。开发时用来肉眼检查画得对不对，
/// 也方便你照着它的样子做自己的牌面图。
/// </summary>
public static class CardSheet
{
    public static Bitmap Render(CardRenderer renderer, Theme theme, int cardHeight = 150, string? caption = null)
    {
        var samples = new[]
        {
            new Card(Suit.Spade, Rank.Ace, 0),
            new Card(Suit.Heart, Rank.King, 0),
            new Card(Suit.Club, Rank.Ten, 0),
            new Card(Suit.Diamond, Rank.Two, 0),
            new Card(Suit.Spade, Rank.Five, 0),
            new Card(Suit.Spade, Rank.Five, 1),
            new Card(Suit.Joker, Rank.SmallJoker, 0),
            new Card(Suit.Joker, Rank.BigJoker, 0),
        };

        var bigWidth = (int)Math.Round(cardHeight * theme.CardAspect);
        var smallHeight = (int)Math.Round(cardHeight * 0.62);
        var smallWidth = (int)Math.Round(smallHeight * theme.CardAspect);

        var padding = 26;
        var width = padding * 2 + samples.Length * (bigWidth + 12) + smallWidth + 40;
        var height = padding * 2 + cardHeight + smallHeight + 120;

        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var backdrop = new LinearGradientBrush(
            new Rectangle(0, 0, width, height), theme.TableInner, theme.TableOuter, 60f))
        {
            g.FillRectangle(backdrop, 0, 0, width, height);
        }

        using var titleFont = new Font("Microsoft YaHei", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(theme.TextPrimary);
        using var mutedBrush = new SolidBrush(theme.TextMuted);

        g.DrawString(caption ?? "自带牌面", titleFont, textBrush, padding, padding);

        var y = padding + 34;
        var x = padding;

        foreach (var card in samples)
        {
            var image = renderer.GetCard(card, bigWidth, cardHeight);
            g.DrawImage(image, x, y);

            var name = card.IsJoker ? card.Rank.Text() : $"{card.Suit.Symbol()}{card.Rank.Text()}";
            g.DrawString(name, labelFont, mutedBrush, x + 2, y + cardHeight + 4);

            var fileName = card.Copy == 1 ? CardFileNames.Canonical(card) : CardFileNames.Canonical(card);
            g.DrawString($"{fileName}.png", labelFont, mutedBrush, x + 2, y + cardHeight + 22);

            x += bigWidth + 12;
        }

        // 小尺寸，看看缩到桌面出牌区那么大时还清不清楚
        g.DrawImage(renderer.GetBack(smallWidth, smallHeight), x + 16, y);
        g.DrawString("牌背", labelFont, mutedBrush, x + 18, y + smallHeight + 4);

        var smallY = y + cardHeight + 48;
        g.DrawString("小尺寸：", labelFont, textBrush, padding, smallY + smallHeight / 2f - 8);

        var smallX = padding + 66;
        foreach (var card in samples.Take(6))
        {
            g.DrawImage(renderer.GetCard(card, smallWidth, smallHeight), smallX, smallY);
            smallX += smallWidth + 8;
        }

        var tip = "自己做样式：把图放进 skins\\样式名\\ 下面，文件名用 ♠A.png / 黑桃A.png / SA.png 都行，缺哪张就用自带画法补";
        g.DrawString(tip, labelFont, mutedBrush, padding, smallY + smallHeight + 16);

        return bitmap;
    }
}
