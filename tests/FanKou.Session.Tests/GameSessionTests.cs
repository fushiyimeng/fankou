using FanKou.Session;

namespace FanKou.Session.Tests;

/// <summary>
/// 驱动层回归测试。场景本身写在 <see cref="SessionDiagnostics"/> 里，
/// 和命令行工具 <c>tools/FanKou.SessionCheck</c> 共用同一份——
/// 分成两份迟早会跑偏（这是这个项目里已经踩过一次的坑）。
/// </summary>
public class GameSessionTests
{
    [Fact]
    public async Task 驱动层的三条主路径都能跑通()
    {
        var result = await SessionDiagnostics.RunAllAsync(seed: 20261016, timeout: TimeSpan.FromSeconds(120));

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Lines));
        Assert.Equal(3, result.Lines.Count);
    }
}
