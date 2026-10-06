using FanKou.Core.Game;

namespace FanKou.Ai;

/// <summary>
/// 把引擎和四个"脑子"接起来：引擎说要决策，就找对应的脑子要一个，再提交回去。
/// 整个过程不碰界面，所以可以直接拿来批量自打。
/// </summary>
public sealed class GameDriver
{
    private readonly IReadOnlyDictionary<Seat, IPlayerBrain> _brains;

    public GameDriver(GameEngine engine, IReadOnlyDictionary<Seat, IPlayerBrain> brains)
    {
        Engine = engine;
        _brains = brains;

        foreach (var seat in Seats.All)
        {
            if (!brains.ContainsKey(seat))
            {
                throw new ArgumentException($"座位 {seat.Name()} 没有配脑子", nameof(brains));
            }
        }
    }

    public GameEngine Engine { get; }

    /// <summary>
    /// 打完一局，返回这一局产生的全部事件。
    /// <paramref name="onRoundStarted"/> 在刚发完牌时回调一次，调试工具用它打印四家手牌。
    /// </summary>
    public IReadOnlyList<GameEvent> RunRound(Action<GameEngine>? onRoundStarted = null)
    {
        var events = new List<GameEvent>(Engine.StartRound());
        onRoundStarted?.Invoke(Engine);

        var guard = 0;
        while (guard++ < 500_000)
        {
            if (Engine.Phase == GamePhase.RoomOver)
            {
                break;
            }

            if (Engine.Pending is { } pending)
            {
                events.AddRange(Engine.Apply(Decide(pending)));
                continue;
            }

            // 首局是发牌途中"抢先叫主"，无界面时不需要节流，直接一次推到底。
            if (Engine.Phase == GamePhase.Dealing)
            {
                events.AddRange(Engine.AdvanceDeal());
                continue;
            }

            break;
        }

        return events;
    }

    public GameCommand Decide(PendingDecision pending) =>
        BrainDecider.Decide(
            _brains[pending.Actor],
            pending,
            Engine.Trump,
            BrainDecider.Describe(Engine));
}
