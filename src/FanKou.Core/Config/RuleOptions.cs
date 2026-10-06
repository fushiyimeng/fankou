namespace FanKou.Core.Config;

/// <summary>
/// 跟对子/拖拉机凑不齐结构时，缺口要不要拿主牌补。四个取值说的是同一件事：
/// **这条义务在什么条件下成立**。
///
/// 依据是规则文档第 102 行（第三次补充）：
/// "有人打对子/拖拉机，自己手上有一张同花色非主的情况下，可以不用给主牌，
/// 但一张都没有的情况下，就必须垫主牌"——对应默认的 <see cref="TrumpOnlyWhenVoid"/>。
///
/// 第 69 行那条"手上只有一张梅花也必须再给一张主"是同一处规则的早期说法，已被第 102 行作废。
/// 但各地可能还有按老规矩打的，所以留着 <see cref="TrumpOnlyWhenPartial"/> 可选。
/// </summary>
public enum FillPolicy
{
    /// <summary>手上**一张该花色都没有**时，缺口才必须用主牌补。（默认，文档第 102 行）</summary>
    TrumpOnlyWhenVoid,

    /// <summary>只要凑不齐结构，缺口就必须用主牌补，哪怕一张该花色都没有。</summary>
    TrumpOnly,

    /// <summary>手上有一部分该花色、但凑不齐结构时，缺口才必须用主牌补。（文档第 69 行的旧规矩）</summary>
    TrumpOnlyWhenPartial,

    /// <summary>缺口随便补。</summary>
    AnyCard,
}

/// <summary>
/// 跟拖拉机的结构要求。规则文档第 90 行"有对子出对子，有连队出连队"，所以默认要求保持连对形状。
/// </summary>
public enum TractorPolicy
{
    /// <summary>手上存在该花色的连对时，必须出连对形状。（默认，按文档）</summary>
    StructurePreserving,

    /// <summary>只要求出够同样数量的对子，不要求连在一起。</summary>
    PairCountOnly,
}

/// <summary>
/// 规则文档里几处存疑或有歧义的点的开关集合，映射到项目根目录的 rules.json。
/// 用户问完长辈后改一行即可，不用改代码。
/// </summary>
public sealed record RuleOptions
{
    /// <summary>
    /// 小王对 + 大王对算不算 2 连对。文档第 65 行说"两对王可以租拖拉机"（算），
    /// 第 89 行用户又亲口标注不确定、待问长辈。默认按前者。
    /// 注意无论开关如何，王对都不能跟花色牌混合连续，三连对及以上也不参与（文档第 67 行）。
    /// </summary>
    public bool JokerPairChainIsTractor { get; init; } = true;

    /// <summary>
    /// 拖拉机的相邻性与比较是否用自然序（级牌不提权）。
    /// 文档第 28 行"连对里面包含的即使主，也没有别的大……你打出 4455，别人打出 6677，你没有别人大"。
    /// 若长辈说主级牌在拖拉机里也最大，把它改 false，改动点只在 IdentityResolver 一处。
    /// </summary>
    public bool TractorUsesNaturalOrder { get; init; } = true;

    /// <summary>跟对子/拖拉机凑不齐时的补牌策略，见 <see cref="FillPolicy"/>。</summary>
    public FillPolicy PairFillerPolicy { get; init; } = FillPolicy.TrumpOnlyWhenVoid;

    /// <summary>跟拖拉机的结构要求，见 <see cref="TractorPolicy"/>。</summary>
    public TractorPolicy TractorFollowPolicy { get; init; } = TractorPolicy.StructurePreserving;

    /// <summary>
    /// 甩牌：同花色的几张散牌一起甩出去。
    ///
    /// 规矩（文档第 27、72 行）：必须同花色；而且**这门里别人手上没有比你更大的牌**才能甩，
    /// 不然就是违规打不出去。线下靠罚则，线上直接由系统拦住（文档第 27 行原话）。
    ///
    /// 特例：这门最大的两张 + 次大的一张（打 5 是 AAK、打 K 是 AAQ）永远甩得出去——
    /// 两副牌每门只有两张 A，谁也凑不出三张来压它（文档第 91 行的道理）。
    /// </summary>
    public bool AllowThrows { get; init; } = true;

    /// <summary>
    /// 反主：对手可以用更高的规格（叫主 &lt; 定主 &lt; 小无主 &lt; 大无主）把主牌抢过去，
    /// 一直可以反到双大王为止。队友不能反，同规格也不能反（文档第 14、55 行）。
    /// </summary>
    public bool AllowCounterCalls { get; init; } = true;

    /// <summary>
    /// 叫主时是否开放小无主（两张小王）和大无主（两张大王）。
    /// 文档第 12、54 行写明了这两种规格，而且次序是
    /// 叫主 &lt; 定主 &lt; 小无主 &lt; 大无主，默认开着。
    /// </summary>
    public bool AllowNoTrumpCalls { get; init; } = true;

    public static RuleOptions Default { get; } = new();

    /// <summary>写回 rules.json 用，格式跟手写的保持一致。</summary>
    public static string ToJson(RuleOptions options) => System.Text.Json.JsonSerializer.Serialize(
        options,
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });

    public static RuleOptions FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<RuleOptions>(json, JsonSettings) ?? Default;

    private static readonly System.Text.Json.JsonSerializerOptions JsonSettings = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
