using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Trump;

namespace FanKou.Core.Bidding;

/// <summary>
/// 一种合法的叫法。一个玩家手上可能同时有好几种，比如同时凑得出"叫主"和"定主"。
/// </summary>
/// <param name="Kind">规格：叫主 / 定主 / 小无主 / 大无主。</param>
/// <param name="TrumpSuit">叫出的主花色；无主时为 null。</param>
/// <param name="ShowCards">
/// 要摆出来的明牌（文档第 56 行："叫牌的牌可以给别人看，这是明牌必须摆出来"）。
/// 这几张牌不能被扣进底牌。
/// </param>
public sealed record CallOption(CallKind Kind, Suit? TrumpSuit, ImmutableArray<Card> ShowCards)
{
    /// <summary>规格高低。第一版只用到叫主(1)和定主(2)。</summary>
    public int Priority => (int)Kind;

    public string Describe() => Kind switch
    {
        CallKind.SuitedSuit => $"叫主 {TrumpSuit!.Value.Symbol()}{LevelText}",
        CallKind.SuitedBoth => $"定主 {TrumpSuit!.Value.Symbol()}{LevelText}",

        // 无主的明牌就是两张王，没有级牌可写，别去硬找。
        CallKind.SmallNoTrump => "小无主（两张小王）",
        CallKind.BigNoTrump => "大无主（两张大王）",
        _ => "?",
    };

    private string LevelText =>
        ShowCards.Where(c => !c.IsJoker).Select(c => c.Rank.Text()).FirstOrDefault() ?? string.Empty;

    public string DescribeWithCards() => $"{Describe()}（明牌：{CardText.Format(ShowCards)}）";
}
