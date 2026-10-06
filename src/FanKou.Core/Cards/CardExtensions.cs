namespace FanKou.Core.Cards;

public static class CardExtensions
{
    /// <summary>
    /// 自然序位次：2 → 0，3 → 1，…，A → 12，小王 → 13，大王 → 14。
    /// 拖拉机的"相邻"和"比大小"都用它，不用 <c>CardIdentity.Order</c>——
    /// 规则文档第 28 行明确说"连对里面包含的即使主，也没有别的大"（4455 打不过 6677）。
    /// </summary>
    public static int NaturalIndex(this Rank rank) => (int)rank - 2;

    public static int Points(this Rank rank) => rank switch
    {
        Rank.Five => 5,
        Rank.Ten or Rank.King => 10,
        _ => 0,
    };

    public static bool IsJoker(this Rank rank) => rank >= Rank.SmallJoker;
}
