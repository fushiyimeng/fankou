namespace FanKou.Core.Util;

/// <summary>
/// 组合枚举。名字叫 Combos 而不是 Combinations，是为了避开 <c>FanKou.Core.Combinations</c> 命名空间。
/// </summary>
public static class Combos
{
    /// <summary>
    /// 从 <paramref name="items"/> 里取 <paramref name="k"/> 个的所有组合。
    /// <paramref name="cap"/> 是硬上限：候选出牌的枚举在某些局面下会爆炸
    /// （比如手上 25 张、要垫 8 张），到达上限就停，宁可少给几个候选也不能卡住界面。
    /// </summary>
    public static IEnumerable<T[]> Of<T>(IReadOnlyList<T> items, int k, int cap = 1000)
    {
        if (k < 0 || k > items.Count)
        {
            yield break;
        }

        if (k == 0)
        {
            yield return [];
            yield break;
        }

        var indices = new int[k];
        for (var i = 0; i < k; i++)
        {
            indices[i] = i;
        }

        var produced = 0;
        while (true)
        {
            var result = new T[k];
            for (var i = 0; i < k; i++)
            {
                result[i] = items[indices[i]];
            }

            yield return result;
            if (++produced >= cap)
            {
                yield break;
            }

            var position = k - 1;
            while (position >= 0 && indices[position] == items.Count - k + position)
            {
                position--;
            }

            if (position < 0)
            {
                yield break;
            }

            indices[position]++;
            for (var i = position + 1; i < k; i++)
            {
                indices[i] = indices[i - 1] + 1;
            }
        }
    }
}
