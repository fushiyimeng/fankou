namespace FanKou.Ai;

/// <summary>人机难度。</summary>
public enum AiLevel
{
    /// <summary>入门：只保证出牌合法，基本是乱打，偶尔抽风。给人练手用。</summary>
    Beginner = 0,

    /// <summary>简单：有一点自己的逻辑，看得懂分牌和队友，但记不住牌。</summary>
    Simple = 1,

    /// <summary>普通：会记牌、算牌。知道外面还剩什么、自己这一手会不会被压。</summary>
    Normal = 2,

    /// <summary>困难：在普通的基础上还会推算——谁空门了、那些能压的牌有多大可能在对家手上。</summary>
    Hard = 3,
}

public static class BrainFactory
{
    public static IPlayerBrain Create(AiLevel level, Core.Util.IRandomSource rng) => level switch
    {
        AiLevel.Beginner => new RandomBrain(rng),
        AiLevel.Normal => new NormalBrain(rng),
        AiLevel.Hard => new HardBrain(rng),
        _ => new SimpleBrain(rng),
    };

    public static string Name(AiLevel level) => level switch
    {
        AiLevel.Beginner => "入门（乱打，只保证合法）",
        AiLevel.Simple => "简单（有一点逻辑，不记牌）",
        AiLevel.Normal => "普通（会记牌、算牌）",
        AiLevel.Hard => "困难（记牌 + 推算对家手牌）",
        _ => "简单",
    };
}
