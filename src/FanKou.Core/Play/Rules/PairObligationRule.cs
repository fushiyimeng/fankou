namespace FanKou.Core.Play.Rules;

/// <summary>
/// 领出对子时，手上只要有对子就必须出对子，哪怕得拆掉连对。
/// 文档第 76 行："如果对手出了对子，你手上有对子，那么必须出对子，
/// 比如黑桃对6，你有黑桃连对7788，那么你都必须拆掉连对，出黑桃77或者88。"
///
/// 注意这个义务**只局限在领出的那个花色里**——文档第 34 行说的是
/// "其它的，也必须先出同花色的对子/拖拉机，如何没有，才可以出其它牌或杀牌"。
/// 所以完全没这个花色时不要求凑对；但**垫主牌的义务是另一码事**，
/// 空门时照样得垫主，见 <see cref="TrumpFillerRule"/>（文档第 102 行）。
/// </summary>
internal sealed class PairObligationRule : IMoveRule
{
    public string Name => "领出对子时有对子必须出对子";

    public string? Check(FollowContext context)
    {
        if (context.Lead.Type != Combinations.ComboType.Pair)
        {
            return null;
        }

        var handOfGroup = context.HandOfLeadGroup;
        if (handOfGroup.Length < context.Lead.Length)
        {
            // 手上这个花色本来就不够两张，义务不成立。
            return null;
        }

        var selectedOfGroup = context.SelectedOfLeadGroup;
        if (selectedOfGroup.Length < 2)
        {
            return null;
        }

        if (FollowStructure.PairedKeys(handOfGroup).Length == 0)
        {
            // 手上压根没有对子，随便出两张这个花色的即可。
            return null;
        }

        // 对子要求同花色同点数。领出主牌时跟牌组里各门花色都有，
        // ♠7 和 ♥7 点数一样却不是一对。
        var isPair = selectedOfGroup[0].Suit == selectedOfGroup[1].Suit
            && selectedOfGroup[0].Rank == selectedOfGroup[1].Rank;

        return isPair ? null : "手上有对子就必须出对子，不能拆开";
    }
}
