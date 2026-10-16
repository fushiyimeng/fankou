using FanKou.Session;

// 无界面的驱动层自检：不依赖 xUnit，服务器上（或 CI 里）直接跑就能知道驱动有没有卡住。
//
//   dotnet run --project tools/FanKou.SessionCheck            # 默认种子
//   dotnet run --project tools/FanKou.SessionCheck -- 7       # 指定种子复现
//
// 退出码 0 = 全过，1 = 有失败。

var seed = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 20261016;

Console.WriteLine($"驱动层自检，种子 {seed}");
Console.WriteLine();

var result = await SessionDiagnostics.RunAllAsync(seed);

foreach (var line in result.Lines)
{
    Console.WriteLine(line);
}

Console.WriteLine();
Console.WriteLine(result.Passed ? "自检通过" : "自检失败");

return result.Passed ? 0 : 1;
