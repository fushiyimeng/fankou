namespace FanKou.Core.Scoring;

/// <summary>一局打完的结果。判据见规则文档第 29、40、41 行。</summary>
public enum RoundOutcome
{
    /// <summary>庄家赢，且闲家一分未得（剃光头）—— 庄家升两级。</summary>
    DeclarerUp2,

    /// <summary>庄家赢 —— 升一级。</summary>
    DeclarerUp1,

    /// <summary>
    /// 闲家 85~114 分：抢主局。这一局谁都没赢（等级和庄闲都不变），
    /// 但**下一局改成发牌途中抢先叫主**，抢到的那队保住等级、没抢到的掉级。
    /// </summary>
    GrabRound,

    /// <summary>闲家 115~119 分：扮权。庄闲对调，双方都不升级。</summary>
    DefenderBanQuan,

    /// <summary>闲家 120~199 分 —— 升一级。</summary>
    DefenderUp1,

    /// <summary>闲家 200 分以上 —— 升两级（超过也只升两级）。</summary>
    DefenderUp2,

    /// <summary>庄家无牌叫不起，自动判负，等级清除，闲家接庄。</summary>
    DeclarerCannotCall,
}

public static class ScoreRules
{
    public const int TotalPoints = 200;

    /// <summary>闲家够这个分升一级。</summary>
    public const int UpOneThreshold = 120;

    /// <summary>闲家够这个分升两级，也是上限。</summary>
    public const int UpTwoThreshold = 200;

    /// <summary>闲家够这个分就是扮权。</summary>
    public const int BanQuanThreshold = 115;

    /// <summary>闲家够这个分就是抢主局。</summary>
    public const int GrabThreshold = 85;

    /// <summary>底牌只有闲家赢下最后一手才算数，倍率按最后一手的牌型。</summary>
    public static int MultiplierFor(Combinations.ComboType lastTrickType) => lastTrickType switch
    {
        Combinations.ComboType.Pair => 4,
        Combinations.ComboType.Tractor => 8,

        // 单张、甩牌、以及凑不成牌型的混合牌，都只有两倍（文档第 58、92 行）。
        _ => 2,
    };
}
