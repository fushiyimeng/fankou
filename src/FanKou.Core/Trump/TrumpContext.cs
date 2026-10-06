using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Config;

namespace FanKou.Core.Trump;

/// <summary>
/// 本局的主牌上下文。一局开打前后就固定不变，所有身份判定都以它为准。
/// </summary>
/// <param name="Kind">叫主的规格。</param>
/// <param name="TrumpSuit">主花色；无主时为 null。</param>
/// <param name="LevelRanks">
/// 本局的级牌点数。第一版恒为单个元素（= 本局庄家那队的等级）。
/// 存成集合是为了抢主局（文档第 86 行，双方各用自家级牌）能零改动扩展。
/// </param>
/// <param name="Options">规则开关。</param>
public sealed record TrumpContext(
    CallKind Kind,
    Suit? TrumpSuit,
    ImmutableSortedSet<Rank> LevelRanks,
    RuleOptions Options)
{
    /// <summary>第一版只有一个级牌，取最小元素即可。</summary>
    public Rank LevelRank => LevelRanks.Min;

    public bool IsNoTrump => Kind is CallKind.SmallNoTrump or CallKind.BigNoTrump;

    public bool HasTrumpSuit => TrumpSuit is not null;

    /// <summary>叫主 / 定主：某花色为主，该花色的级牌是主 5，其余花色的级牌是副 5。</summary>
    public static TrumpContext Suited(Suit trumpSuit, Rank levelRank, RuleOptions? options = null) =>
        new(CallKind.SuitedSuit, trumpSuit, [levelRank], options ?? RuleOptions.Default);

    public static TrumpContext Suited(Suit trumpSuit, Rank levelRank, CallKind kind, RuleOptions options) =>
        new(kind, trumpSuit, [levelRank], options);

    /// <summary>小无主：只有大小王和所有花色的级牌是主（文档第 64 行）。</summary>
    public static TrumpContext SmallNoTrump(Rank levelRank, RuleOptions? options = null) =>
        new(CallKind.SmallNoTrump, null, [levelRank], options ?? RuleOptions.Default);

    /// <summary>大无主：只有大小王是主，级牌不再是主（文档第 65 行）。</summary>
    public static TrumpContext BigNoTrump(Rank levelRank, RuleOptions? options = null) =>
        new(CallKind.BigNoTrump, null, [levelRank], options ?? RuleOptions.Default);

    /// <summary>显示用，例如 "♠5主" / "小无主(5)" / "大无主(5)"。</summary>
    public string Describe() => Kind switch
    {
        CallKind.SuitedSuit or CallKind.SuitedBoth => $"{TrumpSuit!.Value.Symbol()}{LevelRank.Text()}主",
        CallKind.SmallNoTrump => $"小无主({LevelRank.Text()})",
        CallKind.BigNoTrump => $"大无主({LevelRank.Text()})",
        _ => "?",
    };
}
