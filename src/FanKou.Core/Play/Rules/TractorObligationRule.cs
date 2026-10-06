using FanKou.Core.Config;

namespace FanKou.Core.Play.Rules;

/// <summary>
/// 领出拖拉机时，手上能凑几对就得凑几对；文档第 90 行还额外要求"有连队出连队"——
/// 手上存在连对形状时，出的对子也必须是连着的。
///
/// 后一条对应 <see cref="TractorPolicy.StructurePreserving"/>，可以在 rules.json 里关掉。
/// </summary>
internal sealed class TractorObligationRule : IMoveRule
{
    public string Name => "领出拖拉机时有对子/连对必须跟上";

    public string? Check(FollowContext context)
    {
        if (context.Lead.Type != Combinations.ComboType.Tractor)
        {
            return null;
        }

        var handOfGroup = context.HandOfLeadGroup;
        var selectedOfGroup = context.SelectedOfLeadGroup;

        var needPairs = FollowStructure.NeededPairs(
            handOfGroup, context.Lead.PairCount, selectedOfGroup.Length);

        if (FollowStructure.PairCount(selectedOfGroup) < needPairs)
        {
            return $"这是 {context.Lead.PairCount} 连对，手上有对子就得先出对子";
        }

        if (needPairs < 2 || context.Options.TractorFollowPolicy != TractorPolicy.StructurePreserving)
        {
            return null;
        }

        // 手上存在够长的连对，就要求出的对子也连着。
        if (FollowStructure.LongestRun(handOfGroup, context.Trump) >= needPairs
            && !FollowStructure.HasRun(selectedOfGroup, needPairs, context.Trump))
        {
            return "手上有连对就必须出连对，不能拆成散对";
        }

        return null;
    }
}
