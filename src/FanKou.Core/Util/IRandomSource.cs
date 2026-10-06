namespace FanKou.Core.Util;

/// <summary>
/// 所有随机性都从这里取。开局洗牌、首局谁先叫、AI 的随机延迟都用它，
/// 这样同一 seed 能完整复现一局，出问题时可以把 seed 抄进测试里重放。
/// </summary>
public interface IRandomSource
{
    /// <summary>返回 [0, maxExclusive) 的整数。</summary>
    int Next(int maxExclusive);

    /// <summary>返回 [0, 1) 的小数。</summary>
    double NextDouble();
}

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _random;

    public int Seed { get; }

    public SeededRandomSource(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public double NextDouble() => _random.NextDouble();
}

/// <summary>平时用的非确定性随机源。</summary>
public sealed class SharedRandomSource : IRandomSource
{
    public static readonly SharedRandomSource Instance = new();

    private readonly Random _random = new();

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
