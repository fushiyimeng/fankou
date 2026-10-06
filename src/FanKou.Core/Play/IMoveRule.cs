namespace FanKou.Core.Play;

/// <summary>
/// 一条出牌合法性规则。各条规则之间**互不依赖**——每条只看 (手牌, 选的牌, 领出)，
/// 所以可以随意增删、单独测试。
/// </summary>
public interface IMoveRule
{
    string Name { get; }

    /// <summary>通过返回 null，不通过返回给玩家看的中文原因。</summary>
    string? Check(FollowContext context);
}
