using System.Net.Http.Json;
using FanKou.Core.Cards;

namespace FanKou.Web.Services;

/// <summary>
/// 一套牌面素材：把 <see cref="Card"/> 映射成 wwwroot 下的图片地址。
///
/// 桌面版是「按候选文件名去磁盘上挨个找」（<c>CardSkin.FindCardPath</c>），
/// 浏览器里既枚举不了目录、也发不出几十个探测请求，所以改成「按候选名在一份索引里查」。
/// 认名字的规则仍然由 Core 的 <see cref="CardFileNames"/> 提供，两版共用同一套命名契约——
/// 用户自己做好的图，桌面版和网页版都能直接用。
/// </summary>
public sealed class CardAssetSet
{
    private readonly Dictionary<string, string> _urls;

    private CardAssetSet(string name, string author, Dictionary<string, string> urls, string? backUrl)
    {
        Name = name;
        Author = author;
        _urls = urls;
        BackUrl = backUrl;
        CardCount = CountCards();
    }

    /// <summary>样式显示名（来自 index.json，缺省用文件夹名）。</summary>
    public string Name { get; }

    /// <summary>作者/来源（来自 index.json，可能为空）。</summary>
    public string Author { get; }

    /// <summary>牌背的地址；这套素材没提供牌背时为 null。</summary>
    public string? BackUrl { get; }

    /// <summary>这套素材里实际找到了多少张牌（0~54）。</summary>
    public int CardCount { get; }

    /// <summary>
    /// 从 <paramref name="folder"/> 下的 index.json 读入素材清单。
    /// 读不到或格式不对时返回 null——宁可退回程序自带的画法，也不能让换皮肤把游戏搞崩。
    /// </summary>
    public static async Task<CardAssetSet?> LoadAsync(HttpClient http, string folder)
    {
        folder = folder.TrimEnd('/');

        SkinIndex? index;
        try
        {
            index = await http.GetFromJsonAsync<SkinIndex>($"{folder}/index.json");
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }

        if (index?.Files is not { Length: > 0 } files)
        {
            return null;
        }

        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var key = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            // 文件名可能有中文或 ♠♥♣♦，逐段转义后再拼进地址。
            urls[key] = $"{folder}/{Uri.EscapeDataString(file)}";
        }

        return new CardAssetSet(
            index.Name ?? folder,
            index.Author ?? string.Empty,
            urls,
            FindBack(urls));
    }

    /// <summary>这张牌用哪张图；这套素材里没有它就返回 null（调用方应退回自带画法）。</summary>
    public string? UrlFor(Card card)
    {
        foreach (var candidate in CardFileNames.ForCard(card))
        {
            if (_urls.TryGetValue(candidate, out var url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? FindBack(Dictionary<string, string> urls)
    {
        foreach (var candidate in CardFileNames.ForBack())
        {
            if (urls.TryGetValue(candidate, out var url))
            {
                return url;
            }
        }

        return null;
    }

    private int CountCards()
    {
        var count = 0;

        foreach (var suit in Suits.All)
        {
            foreach (var rank in Ranks.Standard)
            {
                if (UrlFor(new Card(suit, rank, 0)) is not null)
                {
                    count++;
                }
            }
        }

        foreach (var rank in new[] { Rank.SmallJoker, Rank.BigJoker })
        {
            if (UrlFor(new Card(Suit.Joker, rank, 0)) is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>index.json 的形状。字段名与桌面版的 <c>skin.json</c> 保持一致，做图的人只用记一套。</summary>
    private sealed record SkinIndex(string? Name, string? Author, string[]? Files);
}
