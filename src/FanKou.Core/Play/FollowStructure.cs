using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Play;

/// <summary>
/// 跟牌时的"对子结构"算术。跟牌义务里最绕的一块都收在这里，方便单独测。
///
/// 有一条必须时刻记住：跟牌的分组（<see cref="FollowGroup"/>）和"能不能成对子"
/// 是两回事。领出主牌时，跟牌组里会同时装着 ♠7、♥7、♣5 各门花色的牌，
/// 但 ♠7 和 ♥7 **不是一对**——对子要求同花色同点数。
/// 所以这里所有结构判断都按 (花色, 点数) 走，绝不按点数单独分组。
/// </summary>
internal static class FollowStructure
{
    /// <summary>这堆牌里能凑出几对（同花色同点数两张算一对，四张算两对）。</summary>
    public static int PairCount(IReadOnlyList<Card> cards) =>
        cards.GroupBy(c => (c.Suit, c.Rank)).Sum(g => g.Count() / 2);

    /// <summary>有对子的那些牌（花色 + 点数）。</summary>
    public static (Suit Suit, Rank Rank)[] PairedKeys(IReadOnlyList<Card> cards) =>
        [.. cards.GroupBy(c => (c.Suit, c.Rank)).Where(g => g.Count() >= 2).Select(g => g.Key)];

    /// <summary>
    /// 最长能连出几对。连对必须同一花色，所以按花色分别算，取最大的那门。
    /// </summary>
    public static int LongestRun(IReadOnlyList<Card> cards, TrumpContext context)
    {
        var longest = 0;
        foreach (var suitGroup in cards.GroupBy(c => c.Suit))
        {
            longest = Math.Max(longest, LongestWindow(Ranks(suitGroup, context)));
        }

        return longest;
    }

    /// <summary>选的牌里有没有 <paramref name="need"/> 个挨着的对子。</summary>
    public static bool HasRun(IReadOnlyList<Card> selected, int need, TrumpContext context) =>
        need <= 0 || LongestRun(selected, context) >= need;

    /// <summary>
    /// 这一手最少得出几对。手上的对子不够时，能出多少算多少；
    /// 但出的张数限制了最多能凑几对（比如只出 3 张，最多只能凑 1 对）。
    /// </summary>
    public static int NeededPairs(IReadOnlyList<Card> handOfGroup, int leadPairs, int selectedCount) =>
        Math.Min(Math.Min(leadPairs, PairCount(handOfGroup)), selectedCount / 2);

    private static int[] Ranks(IEnumerable<Card> cards, TrumpContext context) =>
        [.. cards
            .GroupBy(c => c.Rank)
            .Where(g => g.Count() >= 2)
            .Select(g => IdentityResolver.Resolve(g.First(), context).TractorRank)
            .Distinct()
            .Order()];

    private static int LongestWindow(int[] sortedRanks)
    {
        var longest = 0;
        var current = 0;

        for (var i = 0; i < sortedRanks.Length; i++)
        {
            current = i > 0 && sortedRanks[i] == sortedRanks[i - 1] + 1 ? current + 1 : 1;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}
