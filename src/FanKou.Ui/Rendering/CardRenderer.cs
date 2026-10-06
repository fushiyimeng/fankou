using System.Drawing.Drawing2D;
using FanKou.Core.Cards;

namespace FanKou.Ui.Rendering;

/// <summary>
/// 要一张牌的图，它负责决定用外部样式还是自带画法，并且把结果缓存起来。
///
/// 外部样式**缺哪张就退哪张**：这张牌有图就用图，没有就用程序自带的画法，
/// 不会因为你的样式只做了一半就画不出来。
/// </summary>
public sealed class CardRenderer : IDisposable
{
    private readonly Dictionary<(int CardId, int Width, int Height), Bitmap> _cache = [];
    private readonly Dictionary<string, Bitmap?> _sources = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public CardRenderer(CardSkin? skin = null, Theme? theme = null)
    {
        Skin = skin ?? CardSkin.BuiltIn;
        Theme = theme ?? Theme.Default;
    }

    public CardSkin Skin { get; private set; }

    public Theme Theme { get; }

    /// <summary>这个样式里有多少张牌是用的外部图片（其余走自带画法）。</summary>
    public int ExternalCardCount
    {
        get
        {
            if (Skin.IsBuiltIn)
            {
                return 0;
            }

            var count = 0;
            foreach (var suit in Suits.All)
            {
                foreach (var rank in Ranks.Standard)
                {
                    if (Skin.FindCardPath(new Card(suit, rank, 0)) is not null)
                    {
                        count++;
                    }
                }
            }

            foreach (var rank in new[] { Rank.SmallJoker, Rank.BigJoker })
            {
                if (Skin.FindCardPath(new Card(Suit.Joker, rank, 0)) is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void UseSkin(CardSkin skin)
    {
        Skin = skin;
        ClearCache();
    }

    public Bitmap GetCard(Card card, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var key = (card.Id, width, height);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bitmap = RenderCard(card, width, height);
        _cache[key] = bitmap;
        return bitmap;
    }

    public Bitmap GetBack(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var key = (-1, width, height);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bitmap = CardVisual.RenderBack(width, height, Theme);

        var backPath = Skin.FindBackPath();
        if (backPath is not null && LoadSource(backPath) is { } source)
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(source, FitInside(source.Size, width, height));
        }

        _cache[key] = bitmap;
        return bitmap;
    }

    public void ClearCache()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClearCache();

        foreach (var source in _sources.Values)
        {
            source?.Dispose();
        }

        _sources.Clear();
        _disposed = true;
    }

    private Bitmap RenderCard(Card card, int width, int height)
    {
        var bitmap = CardVisual.RenderFace(card, width, height, Theme);

        if (Skin.FindCardPath(card) is not { } path || LoadSource(path) is not { } source)
        {
            return bitmap;
        }

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, FitInside(source.Size, width, height));

        return bitmap;
    }

    /// <summary>按比例缩放到目标框里居中，不拉伸变形。</summary>
    private static Rectangle FitInside(Size source, int width, int height)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return new Rectangle(0, 0, width, height);
        }

        var scale = Math.Min((float)width / source.Width, (float)height / source.Height);
        var w = (int)Math.Round(source.Width * scale);
        var h = (int)Math.Round(source.Height * scale);

        return new Rectangle((width - w) / 2, (height - h) / 2, w, h);
    }

    /// <summary>
    /// 读图。用流读进内存再复制一份，避免文件被程序锁住——
    /// 用户改图的时候不用先关程序。
    /// </summary>
    private Bitmap? LoadSource(string path)
    {
        if (_sources.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? loaded = null;

        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            loaded = new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            loaded = null;
        }

        _sources[path] = loaded;
        return loaded;
    }
}
