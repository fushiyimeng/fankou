using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Play.Rules;
using FanKou.Core.Trump;

namespace FanKou.Core.Play;

/// <summary>
/// 出牌合法性的**唯一**真相源。界面按钮的灰亮、AI 的候选、日志回放都问它。
/// </summary>
public static class MoveValidator
{
    // 顺序只影响"先报哪条错误"，不影响判定结果——各条规则互不依赖。
    private static readonly IMoveRule[] Rules =
    [
        new OwnCardsRule(),
        new CardCountRule(),
        new FollowSuitRule(),
        new PairObligationRule(),
        new TractorObligationRule(),
        new TrumpFillerRule(),
    ];

    public static ValidationResult Validate(FollowContext context)
    {
        foreach (var rule in Rules)
        {
            var reason = rule.Check(context);
            if (reason is not null)
            {
                return ValidationResult.Fail(reason);
            }
        }

        return ValidationResult.Ok;
    }

    /// <summary>方便调用的重载：手牌和选牌用普通数组或列表传就行。</summary>
    public static ValidationResult Validate(
        CardCombination lead,
        IReadOnlyList<Card> hand,
        IReadOnlyList<Card> selected,
        TrumpContext trump) =>
        Validate(new FollowContext(lead, [.. hand], [.. selected], trump));

    /// <summary>领出永远是合法的，只要能认成牌型。</summary>
    public static ValidationResult ValidateLead(CardCombination lead) =>
        lead.Type == ComboType.Invalid
            ? ValidationResult.Fail("这手牌不成牌型")
            : ValidationResult.Ok;

    public static bool IsValid(FollowContext context) => Validate(context).IsValid;
}
