using FanKou.Ai;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 各地规矩不一样的地方都做成了开关。这里验证开关真的能改，
/// 而且是**从下一局生效**——正在打的那局主牌上下文早就定死了，中途改会前后不一致。
/// </summary>
public class RuleSwitchTests
{
    private static GameEngine NewEngine(int seed, out GameDriver driver)
    {
        var rng = new SeededRandomSource(seed);
        var engine = new GameEngine(rng);
        var brains = Seats.All.ToDictionary(
            seat => seat,
            _ => (IPlayerBrain)new SimpleBrain(rng));

        driver = new GameDriver(engine, brains);
        return engine;
    }

    /// <summary>推到定完主为止。</summary>
    private static void PlayUntilTrumpIsSet(GameEngine engine)
    {
        var guard = 0;
        while (engine.Trump is null && guard++ < 300)
        {
            switch (engine.Pending)
            {
                case PendingDecision.NeedCall call:
                    engine.Apply(new DeclareCall(call.Actor, call.Options[0]));
                    continue;
                case PendingDecision.NeedBottomSwap:
                    return;
                default:
                    break;
            }

            if (engine.Phase == GamePhase.Dealing)
            {
                engine.AdvanceDeal();
                continue;
            }

            return;
        }
    }

    [Fact]
    public void 改了开关下一局才生效()
    {
        var engine = NewEngine(7, out var driver);
        driver.RunRound();
        Assert.Equal(GamePhase.RoundOver, engine.Phase);

        engine.UpdateOptions(RuleOptions.Default with { JokerPairChainIsTractor = false });

        engine.StartRound();
        PlayUntilTrumpIsSet(engine);

        Assert.NotNull(engine.Trump);
        Assert.False(engine.Trump!.Options.JokerPairChainIsTractor);
    }

    [Fact]
    public void 正在打的那一局不受开关改动影响()
    {
        var engine = NewEngine(11, out _);

        engine.StartRound();
        PlayUntilTrumpIsSet(engine);

        var trumpBefore = engine.Trump!;
        var flagBefore = trumpBefore.Options.JokerPairChainIsTractor;

        // 打到一半改规则
        engine.UpdateOptions(RuleOptions.Default with { JokerPairChainIsTractor = !flagBefore });

        // 本局的主牌上下文是叫主时就定死的，不该被改掉
        Assert.Equal(flagBefore, engine.Trump!.Options.JokerPairChainIsTractor);
        Assert.Same(trumpBefore, engine.Trump);
    }

    [Fact]
    public void 开关能写回json再读回来()
    {
        var original = RuleOptions.Default with
        {
            JokerPairChainIsTractor = false,
            TractorUsesNaturalOrder = false,
            AllowNoTrumpCalls = false,
        };

        var restored = RuleOptions.FromJson(RuleOptions.ToJson(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void 关掉王对连对之后王对就只是两对()
    {
        // 开关一关，两张小王 + 两张大王不再是一手连对，而是两对分开出。
        var open = RuleOptions.Default with { JokerPairChainIsTractor = true };
        var closed = RuleOptions.Default with { JokerPairChainIsTractor = false };

        var jokers = TestCards.Parse("小王 小王 大王 大王");

        Assert.True(ComboRecognizerAccepts(jokers, open));
        Assert.False(ComboRecognizerAccepts(jokers, closed));
    }

    private static bool ComboRecognizerAccepts(
        FanKou.Core.Cards.Card[] cards,
        RuleOptions options)
    {
        var trump = FanKou.Core.Trump.TrumpContext.Suited(
            FanKou.Core.Cards.Suit.Spade,
            FanKou.Core.Cards.Rank.Five,
            options);

        return FanKou.Core.Combinations.ComboRecognizer.TryRecognize(cards, trump, out _);
    }
}
