using FanKou.Core.Cards;

namespace FanKou.Core.Play.Rules;

/// <summary>选的牌必须都在自己手上（防伪造，也防 AI 越界）。</summary>
internal sealed class OwnCardsRule : IMoveRule
{
    public string Name => "出的牌必须在手上";

    public string? Check(FollowContext context) =>
        CardSet.ContainsAll(context.Hand, context.Selected) ? null : "出的牌不在自己手上";
}

/// <summary>出的张数必须和领出方一样（文档第 36 行："出牌的出了几张，那么其它几家也必须出一样张数的牌，这是必须"）。</summary>
internal sealed class CardCountRule : IMoveRule
{
    public string Name => "张数必须与领出方一致";

    public string? Check(FollowContext context) =>
        context.Selected.Length == context.Lead.Length
            ? null
            : $"领出方出了 {context.Lead.Length} 张，你也得出 {context.Lead.Length} 张（现在选了 {context.Selected.Length} 张）";
}
