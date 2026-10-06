namespace FanKou.Core.Scoring;

public static class RoundResultEvaluator
{
    /// <summary>
    /// 按闲家这一局拿到的总分（含底牌加成）判断结果。
    /// 文档第 29 行："败家足够120分就升1级，200分是2级，115分就是扮权，85分以上是抢主。"
    /// 文档第 40 行："如果这局赢了，闲家一分未得，也就是剃光头了，则庄家升两级。"
    /// </summary>
    public static RoundOutcome Evaluate(int defenderPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(defenderPoints);

        if (defenderPoints >= ScoreRules.UpTwoThreshold)
        {
            return RoundOutcome.DefenderUp2;
        }

        if (defenderPoints >= ScoreRules.UpOneThreshold)
        {
            return RoundOutcome.DefenderUp1;
        }

        if (defenderPoints >= ScoreRules.BanQuanThreshold)
        {
            return RoundOutcome.DefenderBanQuan;
        }

        if (defenderPoints >= ScoreRules.GrabThreshold)
        {
            return RoundOutcome.GrabRound;
        }

        // 闲家没到 85 分，庄家赢；一分未得就是剃光头，升两级。
        return defenderPoints == 0 ? RoundOutcome.DeclarerUp2 : RoundOutcome.DeclarerUp1;
    }

    public static string Describe(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.DeclarerUp2 => "庄家赢，闲家剃光头，庄家升两级",
        RoundOutcome.DeclarerUp1 => "庄家赢，升一级",
        RoundOutcome.GrabRound =>
            "闲家 85~114 分，抢主局：谁都没赢，下一局发牌途中抢先叫主（双方各用自家的级牌去抢）",
        RoundOutcome.DefenderBanQuan => "闲家 115~119 分，扮权：庄闲对调，都不升级",
        RoundOutcome.DefenderUp1 => "闲家升一级",
        RoundOutcome.DefenderUp2 => "闲家升两级",
        RoundOutcome.DeclarerCannotCall => "庄家无牌叫不起，自动判负，闲家接庄",
        _ => "?",
    };
}
