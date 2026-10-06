using System.Globalization;
using System.Text;
using System.Text.Json;
using FanKou.Ai;

// 两档 AI 的强度尺子。
//
//   dotnet run -c Release --project tools/FanKou.Bench -- --a hard --b normal --seeds 1000
//
// 判据、配对方式、置信区间都在 PairedEvaluation 里，测试和这个工具共用同一份。
// 这里只负责：解析参数、打印、写结果文件。
//
// 参数：
//   --a <档>      强档，默认 hard
//   --b <档>      对照档，默认 normal
//   --seeds <n>   配对种子数，默认 1000
//   --rounds <n>  每个种子打几局，默认 8
//   --out <路径>  结果 JSON 写到哪，默认 bench-result.json
//   --quiet       只打一行结论（丢到服务器上跑的时候用）

Console.OutputEncoding = Encoding.UTF8;

var aLevel = AiLevel.Hard;
var bLevel = AiLevel.Normal;
var seeds = 1000;
var rounds = 8;
var outPath = "bench-result.json";
var quiet = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--a" when i + 1 < args.Length:
            aLevel = Enum.Parse<AiLevel>(args[++i], ignoreCase: true);
            break;
        case "--b" when i + 1 < args.Length:
            bLevel = Enum.Parse<AiLevel>(args[++i], ignoreCase: true);
            break;
        case "--seeds" when i + 1 < args.Length:
            seeds = int.Parse(args[++i]);
            break;
        case "--rounds" when i + 1 < args.Length:
            rounds = int.Parse(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--quiet":
            quiet = true;
            break;
    }
}

if (!quiet)
{
    Console.WriteLine($"双扣强度对照 · {BrainFactory.Name(aLevel)} vs {BrainFactory.Name(bLevel)}");
    Console.WriteLine($"配对 {seeds} 个种子 × 每个种子 {rounds} 局 × 换边打两遍");
    Console.WriteLine();
}

var started = DateTime.Now;
var result = PairedEvaluation.Run(aLevel, bLevel, seeds, rounds);
var elapsed = DateTime.Now - started;
var (low, high) = result.Interval;

var summary = result.Decisive == 0
    ? $"没有分出胜负的局（平 {result.Ties}）"
    : $"{BrainFactory.Name(aLevel)} 对 {BrainFactory.Name(bLevel)} 胜率 {result.WinRate * 100:F1}%"
      + $"（95% 置信区间 {low * 100:F1}% ~ {high * 100:F1}%）"
      + $"，有效 {result.Decisive} 局，平 {result.Ties} 局，用时 {elapsed.TotalSeconds:F0} 秒";

Console.WriteLine(summary);

await File.WriteAllTextAsync(
    outPath,
    JsonSerializer.Serialize(
        new
        {
            a = BrainFactory.Name(aLevel),
            b = BrainFactory.Name(bLevel),
            seeds,
            rounds,
            result.AWins,
            result.BWins,
            result.Ties,
            result.Decisive,
            winRate = Math.Round(result.WinRate, 4),
            ciLow = Math.Round(low, 4),
            ciHigh = Math.Round(high, 4),
            seconds = Math.Round(elapsed.TotalSeconds, 1),
            ranAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        },
        new JsonSerializerOptions { WriteIndented = true }));

if (!quiet)
{
    Console.WriteLine($"结果已写入 {outPath}");

    var verdict = low > 0.5
        ? $"→ {BrainFactory.Name(aLevel)} 明显更强（区间下限也在 50% 以上）"
        : high < 0.5
            ? $"→ {BrainFactory.Name(aLevel)} 明显更弱（区间上限也在 50% 以下）"
            : "→ 两者强弱分不出来，差距还在噪声里";

    Console.WriteLine(verdict);
}

return 0;
