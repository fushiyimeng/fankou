using FanKou.Core.Config;

namespace FanKou.Core.Play.Rules;

/// <summary>
/// 跟对子/拖拉机凑不齐时，缺口要不要拿主牌补。触发条件看
/// <see cref="RuleOptions.PairFillerPolicy"/>，默认**空门才强制**。
///
/// 依据是规则文档第 102 行："有人打对子/拖拉机，自己手上有一张同花色非主的情况下，
/// 可以不用给主牌，但一张都没有的情况下，就必须垫主牌"。
/// 注意第 69 行那条"手上只有一张梅花也必须再给一张主"是同一处的早期说法，已被第 102 行作废。
///
/// 只作用于对子和拖拉机。单张、甩牌不要求垫主（文档第 31、72 行）。
/// </summary>
internal sealed class TrumpFillerRule : IMoveRule
{
    public string Name => "对子/拖拉机的缺口按策略垫主牌";

    /// <summary>
    /// 这条义务在什么条件下成立。<see cref="MoveGenerator"/> 生成候选时要跟校验保持一致，
    /// 所以判据放这儿共用一份——两边分家会让"提示"给出的牌和"出牌"按钮的判定对不上。
    /// </summary>
    internal static bool MustFillWithTrump(FillPolicy policy, bool hasLeadGroup) => policy switch
    {
        FillPolicy.AnyCard => false,
        FillPolicy.TrumpOnly => true,
        FillPolicy.TrumpOnlyWhenPartial => hasLeadGroup,
        FillPolicy.TrumpOnlyWhenVoid => !hasLeadGroup,
        _ => false,
    };

    public string? Check(FollowContext context)
    {
        if (context.Lead.Type is not (Combinations.ComboType.Pair or Combinations.ComboType.Tractor))
        {
            return null;
        }

        var handOfGroup = context.HandOfLeadGroup.Length;

        // 这个花色给得起整手，压根没有缺口。
        if (handOfGroup >= context.Lead.Length)
        {
            return null;
        }

        var gap = context.Lead.Length - context.SelectedOfLeadGroup.Length;
        if (gap <= 0)
        {
            return null;
        }

        if (!MustFillWithTrump(context.Options.PairFillerPolicy, handOfGroup > 0))
        {
            return null;
        }

        // 手上主牌（去掉已经算在领出花色里的那几张）够填多少缺口。
        // 不够也不是整条放行——有主牌就得尽量垫出去（文档第 69 行"哪怕你的主只剩下大王了，
        // 依旧需要白搭进入"说的就是这个意思）。
        var availableTrump = context.Hand.Count(context.IsTrump)
            - context.SelectedOfLeadGroup.Count(context.IsTrump);
        var needTrump = Math.Min(gap, availableTrump);

        return context.Selected.Count(context.IsTrump) >= needTrump
            ? null
            : "凑不够这个花色的对子，缺口必须用主牌垫";
    }
}
