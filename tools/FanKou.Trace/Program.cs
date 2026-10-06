using System.Text;
using FanKou.Ai;
using FanKou.Core.Cards;
using FanKou.Core.Game;
using FanKou.Core.Levels;
using FanKou.Core.Util;

// 规则自检工具：四个 AI 随机打，把过程用中文逐轮打出来，
// 方便人眼核对"跟牌义务、谁大谁小、分数归谁"这些规则有没有理解歪。
//
//   dotnet run --project tools/FanKou.Trace -- --seed 123 --hands
//
// 参数：
//   --seed N    指定随机种子（同一个种子每次跑出来完全一样）
//   --rounds N  连打几局
//   --hands     把四家手牌也打出来

Console.OutputEncoding = Encoding.UTF8;

var seed = 20261006;
var rounds = 1;
var showHands = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed" when i + 1 < args.Length:
            seed = int.Parse(args[++i]);
            break;
        case "--rounds" when i + 1 < args.Length:
            rounds = int.Parse(args[++i]);
            break;
        case "--hands":
            showHands = true;
            break;
    }
}

Console.WriteLine($"双扣规则自检 · 种子 {seed} · {rounds} 局");
Console.WriteLine();

var rng = new SeededRandomSource(seed);
var engine = new GameEngine(rng);
var brains = Seats.All.ToDictionary(seat => seat, _ => (IPlayerBrain)new RandomBrain(rng));
var driver = new GameDriver(engine, brains);

for (var round = 0; round < rounds && engine.Phase != GamePhase.RoomOver; round++)
{
    var events = driver.RunRound(e =>
    {
        if (showHands)
        {
            PrintHands(e);
        }
    });

    PrintEvents(events);
}

PrintSummary(engine);
return 0;

static void PrintHands(GameEngine engine)
{
    Console.WriteLine("发牌：");
    foreach (var seat in Seats.All)
    {
        Console.WriteLine($"  {seat.Name()} 家（{seat.TeamOf().Name()}）：{FormatHand(engine.HandOf(seat))}");
    }

    Console.WriteLine();
}

static void PrintEvents(IReadOnlyList<GameEvent> events)
{
    var inTrick = false;
    var nextTrickNumber = 1;

    foreach (var e in events)
    {
        if (e is CardPlayed)
        {
            if (!inTrick)
            {
                Console.WriteLine($"  ── 第 {nextTrickNumber} 轮 ──");
                inTrick = true;
            }

            Console.WriteLine($"    {e.Describe()}");
            continue;
        }

        if (e is TrickWon won)
        {
            inTrick = false;
            nextTrickNumber = won.TrickNumber + 1;
            Console.WriteLine($"    → {e.Describe()}");
            Console.WriteLine();
            continue;
        }

        if (e is RoundStarted or LevelsChanged or BottomRevealed or RoundSettled)
        {
            Console.WriteLine();
        }

        Console.WriteLine(e.Describe());

        if (e is RoundStarted or BottomExchanged or RoundSettled)
        {
            Console.WriteLine();
        }
    }

    Console.WriteLine();
}

static void PrintSummary(GameEngine engine)
{
    Console.WriteLine("────────── 房间状态 ──────────");
    foreach (var team in new[] { Team.EastWest, Team.NorthSouth })
    {
        var index = engine.LevelOf(team);
        var protection = LevelTrack.IsProtected(index) ? "（已过 2，掉级无效）" : "（未过 2，输球掉回 5）";
        Console.WriteLine($"  {team.Name()}：打 {LevelTrack.Describe(index)} {protection}");
    }

    Console.WriteLine($"  本局庄家：{engine.DeclarerTeam.Name()}");
    Console.WriteLine($"  已打局数：{engine.RoundNumber}");

    if (engine.Phase == GamePhase.RoomOver)
    {
        Console.WriteLine($"  ★ {engine.RoomWinner.Name()} 打到 A 又赢一局，整个房间结束");
    }
}

static string FormatHand(IReadOnlyList<Card> hand)
{
    var parts = new List<string>();

    foreach (var suitGroup in hand.Where(c => !c.IsJoker).GroupBy(c => c.Suit).OrderBy(g => g.Key))
    {
        var ranks = string.Join(" ", suitGroup.OrderBy(c => c.Rank).Select(c => c.Rank.Text()));
        parts.Add($"{suitGroup.Key.Symbol()}[{ranks}]  ");
    }

    foreach (var jokerGroup in hand.Where(c => c.IsJoker).GroupBy(c => c.Rank).OrderBy(g => g.Key))
    {
        parts.Add(string.Join(" ", jokerGroup.Select(c => c.Rank.Text())));
    }

    return string.Concat(parts).TrimEnd();
}
