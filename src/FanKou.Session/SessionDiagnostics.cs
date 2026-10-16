using FanKou.Ai;
using FanKou.Core.Config;
using FanKou.Core.Game;

namespace FanKou.Session;

/// <summary>一次自检的结果：过没过，以及每一步到底跑成了什么样。</summary>
public sealed record SessionCheckResult(bool Passed, IReadOnlyList<string> Lines);

/// <summary>
/// 驱动层的无界面自检。
///
/// <see cref="GameSession"/> 替代的是桌面版那 4 个 WinForms 计时器，最难查的故障是"卡住不动"——
/// 某个等待没人唤醒，界面上却一片安静。所以这里把三条主路径各跑一遍：
/// 全托管、真人逐步提交、撤回。xUnit（<c>GameSessionTests</c>）和命令行工具
/// （<c>tools/FanKou.SessionCheck</c>）共用这一份，免得两边的场景慢慢跑偏。
/// </summary>
public static class SessionDiagnostics
{
    public static async Task<SessionCheckResult> RunAllAsync(int seed = 20261016, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(120);
        var lines = new List<string>();
        var passed = true;

        passed &= await CheckAsync("托管连打三局", lines, () => AutoPlayAsync(seed, limit));
        passed &= await CheckAsync("真人逐步提交打一局", lines, () => HumanDrivenAsync(seed + 1, limit));
        passed &= await CheckAsync("撤回退回出牌前", lines, () => UndoAsync(seed + 2, limit));

        return new SessionCheckResult(passed, lines);
    }

    private static async Task<bool> CheckAsync(string name, List<string> lines, Func<Task<string>> body)
    {
        try
        {
            var detail = await body();
            lines.Add($"✅ {name}：{detail}");
            return true;
        }
        catch (Exception ex)
        {
            lines.Add($"❌ {name}：{ex.Message}");
            return false;
        }
    }

    /// <summary>节拍调到最小，自检不该把时间花在"等 AI 思考"上。</summary>
    private static GameSession Fast(int seed, AiLevel level = AiLevel.Normal)
    {
        var session = GameSession.CreateSinglePlayer(level, RuleOptions.Default, Seat.South, seed);
        session.AiThinkDelayMs = 1;
        session.TrickPauseMs = 1;
        session.DealRoundMs = 1;
        session.HumanTimeoutSeconds = 5;
        return session;
    }

    private static async Task<string> AutoPlayAsync(int seed, TimeSpan limit)
    {
        using var session = Fast(seed);
        session.SetAutoPlay(true);

        var done = new TaskCompletionSource<int>();
        var played = 0;

        session.RoundFinished += summary =>
        {
            played++;

            if (summary.RoomOver || played >= 3)
            {
                done.TrySetResult(played);
            }
            else
            {
                session.StartNextRound();
            }
        };

        session.Start();

        if (await Task.WhenAny(done.Task, Task.Delay(limit)) != done.Task)
        {
            throw new InvalidOperationException($"没跑完 3 局（已完成 {played} 局），驱动循环可能卡住了");
        }

        var rounds = await done.Task;

        if (session.Log.Count == 0)
        {
            throw new InvalidOperationException("牌局记录是空的");
        }

        return $"打完 {rounds} 局，记录 {session.Log.Count} 条，等级 东西 {session.Engine.LevelOf(Team.EastWest)} / 南北 {session.Engine.LevelOf(Team.NorthSouth)}";
    }

    private static async Task<string> HumanDrivenAsync(int seed, TimeSpan limit)
    {
        using var session = Fast(seed);

        var calls = 0;
        var swaps = 0;
        var plays = 0;

        session.HumanTurnChanged += () =>
        {
            switch (session.HumanPending)
            {
                case PendingDecision.NeedCall needCall:
                    calls++;
                    var option = needCall.Options.Count > 0 ? needCall.Options[^1] : null;
                    if (option is not null || needCall.CanPass)
                    {
                        session.SubmitCall(option);
                    }

                    break;

                case PendingDecision.NeedBottomSwap swap:
                    swaps++;
                    session.SubmitBottomDiscards(swap.Pool.Take(8).ToList());
                    break;

                case PendingDecision.NeedPlay play when play.Candidates.Count > 0:
                    plays++;
                    session.SubmitPlay(play.Candidates[0].Cards);
                    break;
            }
        };

        var done = new TaskCompletionSource();
        session.RoundFinished += _ => done.TrySetResult();
        session.Start();

        if (await Task.WhenAny(done.Task, Task.Delay(limit)) != done.Task)
        {
            throw new InvalidOperationException("一局没打完");
        }

        if (calls == 0)
        {
            throw new InvalidOperationException("真人一次叫主的机会都没轮到过");
        }

        if (plays < 10)
        {
            throw new InvalidOperationException($"真人只出了 {plays} 手，一局有 25 墩，明显偏少");
        }

        return $"叫主 {calls} 次、换底 {swaps} 次、出牌 {plays} 手";
    }

    private static async Task<string> UndoAsync(int seed, TimeSpan limit)
    {
        using var session = Fast(seed);
        var ready = new TaskCompletionSource();

        session.HumanTurnChanged += () =>
        {
            if (session.HumanPending is PendingDecision.NeedPlay play && play.Candidates.Count > 0)
            {
                ready.TrySetResult();
            }
        };

        session.Start();

        if (await Task.WhenAny(ready.Task, Task.Delay(limit)) != ready.Task)
        {
            throw new InvalidOperationException("没等到真人出牌的机会");
        }

        var handBefore = session.HumanHand.Count;
        var logBefore = session.Log.Count;

        if (session.CanUndo)
        {
            throw new InvalidOperationException("还没出牌就能撤回");
        }

        var combo = session.HumanCandidates[0];
        session.SubmitPlay(combo.Cards);

        if (!session.CanUndo)
        {
            throw new InvalidOperationException("出完一手之后不能撤回");
        }

        // 从这里到断言之间不能 await——中间让出线程的话，AI 会接着出牌，
        // 日志就不是"撤回之后"的样子了。
        if (!session.Undo())
        {
            throw new InvalidOperationException("撤回返回了 false");
        }

        if (session.HumanHand.Count != handBefore)
        {
            throw new InvalidOperationException($"撤回后手牌是 {session.HumanHand.Count} 张，应该是 {handBefore} 张");
        }

        if (session.Log.Count != logBefore)
        {
            throw new InvalidOperationException($"撤回后记录是 {session.Log.Count} 条，应该是 {logBefore} 条");
        }

        // 撤回之后必须停下来等真人。以前这里有个 bug：撤回时读秒没重新起，
        // _humanSecondsLeft 还是 0，下一秒就被判成超时、电脑立刻替你出牌——
        // 玩家看到的就是"一撤回，牌自己又打出去了"。
        var handAfterUndo = session.HumanHand.Count;
        await Task.Delay(1500);

        if (session.HumanHand.Count != handAfterUndo)
        {
            throw new InvalidOperationException("撤回之后电脑自己接着出牌了（读秒没重新起）");
        }

        return $"出 {combo.Length} 张后撤回，手牌 {handBefore} 张、记录 {logBefore} 条都还原了，之后一直等着人操作";
    }
}
