namespace FanKou.Core.Play.Rules;

/// <summary>
/// 有领出的花色就必须先给这个花色，有多少给多少（不够的张数才轮到垫牌）。
/// 文档第 31 行（领出单张）、第 34 行（领出对子/拖拉机）。
///
/// 这里最容易踩的坑：一张牌属于哪一组，**不是看它的花色**，而是看它是不是主牌。
/// 主黑桃打 5 时，♣5 是副主，它属于主牌组、不属于梅花组——
/// 所以别人出梅花时，你手上的 ♣5 不用给出去（详见 <see cref="Trump.FollowGroup"/>）。
/// </summary>
internal sealed class FollowSuitRule : IMoveRule
{
    public string Name => "有该花色必须优先给该花色";

    public string? Check(FollowContext context)
    {
        var handOfGroup = context.HandOfLeadGroup.Length;
        var selectedOfGroup = context.SelectedOfLeadGroup.Length;

        // 给得出多少就给多少，但不超过领出的张数。
        var required = Math.Min(handOfGroup, context.Lead.Length);

        if (selectedOfGroup == required)
        {
            return null;
        }

        var suit = context.LeadGroupName;

        if (required == handOfGroup && handOfGroup < context.Lead.Length)
        {
            return selectedOfGroup < required
                ? $"手上的 {suit} 只有 {handOfGroup} 张，得全出"
                : $"只能出 {handOfGroup} 张 {suit}，多出来的要用别的花色凑";
        }

        return $"必须出 {required} 张 {suit}（现在只出了 {selectedOfGroup} 张）";
    }
}
