using System.Text.Json;
using FanKou.Core.Cards;
using FanKou.Core.Util;

namespace FanKou.Ui.Rendering;

/// <summary>
/// 一套卡牌样式。可以理解成"一个放着牌面图的文件夹"。
///
/// 目录结构（两处都会被扫到）：
/// <code>
///   &lt;exe 所在目录&gt;\skins\我的风格\♠A.png
///   %APPDATA%\FanKou\skins\我的风格\♠A.png
/// </code>
///
/// 文件名怎么写都行，见 <see cref="CardFileNames"/>。缺哪张牌就用程序自带的画法顶上，
/// 所以你可以只做几张图先试试效果，不会因为缺图就崩。
/// </summary>
public sealed class CardSkin
{
    private readonly Dictionary<string, string> _files;

    private CardSkin(string name, string? folder, string? author, Dictionary<string, string> files)
    {
        Name = name;
        Folder = folder;
        Author = author;
        _files = files;
    }

    /// <summary>程序自带的那一套（用代码画的）。</summary>
    public static CardSkin BuiltIn { get; } =
        new("程序自带", null, "FanKou", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public string Name { get; }

    /// <summary>样式所在目录；自带样式为 null。</summary>
    public string? Folder { get; }

    public string? Author { get; }

    public bool IsBuiltIn => Folder is null;

    /// <summary>这个文件夹里认出了多少张图。</summary>
    public int FileCount => _files.Count;

    /// <summary>放在哪个根目录下扫描样式。两处都扫，同名以先扫到的为准。</summary>
    public static IEnumerable<string> DefaultRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "skins");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, "FanKou", "skins");
        }
    }

    /// <summary>扫描所有根目录，返回"程序自带" + 找到的每一套样式。</summary>
    public static IReadOnlyList<CardSkin> Discover(params IEnumerable<string>[] rootSets)
    {
        var roots = rootSets.Length == 0 ? [DefaultRoots()] : rootSets;
        var skins = new List<CardSkin> { BuiltIn };
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BuiltIn.Name };

        foreach (var root in roots.SelectMany(r => r))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            // 根目录本身就是一套样式
            if (TryLoad(root, out var fromRoot) && seenNames.Add(fromRoot.Name))
            {
                skins.Add(fromRoot);
            }

            foreach (var dir in SafeEnumerateDirectories(root))
            {
                if (TryLoad(dir, out var skin) && seenNames.Add(skin.Name))
                {
                    skins.Add(skin);
                }
            }
        }

        return skins;
    }

    public string? FindCardPath(Card card) => FindByNames(CardFileNames.ForCard(card));

    public string? FindBackPath() => FindByNames(CardFileNames.ForBack());

    private static bool TryLoad(string folder, out CardSkin skin)
    {
        skin = BuiltIn!;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in SafeEnumerateFiles(folder))
        {
            if (!CardFileNames.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = Path.GetFileNameWithoutExtension(path);
            files.TryAdd(key, path);
        }

        // 一张图都没有的目录不算样式，免得把 skins 下面的杂文件夹也列出来。
        if (files.Count == 0)
        {
            return false;
        }

        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string? author = null;

        var manifest = Path.Combine(folder, CardFileNames.ManifestFileName);
        if (File.Exists(manifest))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<SkinManifest>(
                    File.ReadAllText(manifest),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (!string.IsNullOrWhiteSpace(meta?.Name))
                {
                    name = meta!.Name!;
                }

                author = meta?.Author;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // 说明文件坏了不影响用图，忽略。
            }
        }

        skin = new CardSkin(name, folder, author, files);
        return true;
    }

    private string? FindByNames(IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (_files.TryGetValue(candidate, out var path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public override string ToString() =>
        IsBuiltIn ? Name : $"{Name}（{FileCount} 张图）";

    private sealed record SkinManifest(string? Name, string? Author);
}
