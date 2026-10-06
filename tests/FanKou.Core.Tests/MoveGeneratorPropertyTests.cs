using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Play;
using FanKou.Core.Trump;
using FanKou.Core.Util;

namespace FanKou.Core.Tests;

/// <summary>
/// 这一组是整个项目最有价值的测试：它把"枚举候选出牌"和"判定合法性"这两份
/// 各自独立写出来的逻辑对撞。只要有一边漂了，这里立刻红。
/// </summary>
public class MoveGeneratorPropertyTests
{
    private static readonly TrumpContext Trump = TrumpContext.Suited(Suit.Spade, Rank.Five);

    private static ImmutableArray<Card> Hand(int size, int seed) =>
        [.. Deck.CreateShuffled(new SeededRandomSource(seed)).Take(size)];

    /// <summary>把所有 C(手牌, 张数) 的组合都拿出来过一遍校验器，作为金标准。</summary>
    private static HashSet<string> BruteForce(
        CardCombination lead,
        ImmutableArray<Card> hand,
        TrumpContext context)
    {
        var valid = new HashSet<string>();
        foreach (var subset in Combos.Of(hand.ToArray(), lead.Length, int.MaxValue))
        {
            if (MoveValidator.Validate(lead, hand, [.. subset], context).IsValid)
            {
                valid.Add(CardSet.ShapeKey(subset));
            }
        }

        return valid;
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 9)]
    [InlineData(3, 10)]
    [InlineData(4, 10)]
    [InlineData(5, 8)]
    public void 生成的候选与暴力枚举完全一致(int seed, int handSize)
    {
        var hand = Hand(handSize, seed);

        // 领出方用另一副手牌，这样能覆盖"我完全没这个花色"的局面。
        var leadSource = Hand(handSize, seed + 1000);

        var leads = MoveGenerator.GenerateLeads(leadSource, Trump)
            .Where(l => l.Length <= 4)   // 控制暴力枚举的规模
            .ToArray();

        Assert.NotEmpty(leads);

        foreach (var lead in leads)
        {
            var expected = BruteForce(lead, hand, Trump);
            var actual = MoveGenerator.GenerateFollows(lead, hand, Trump)
                .Select(c => CardSet.ShapeKey(c.Cards))
                .ToHashSet();

            var missing = expected.Except(actual).ToArray();
            var extra = actual.Except(expected).ToArray();

            Assert.True(
                missing.Length == 0 && extra.Length == 0,
                $"领出「{lead.Describe()}」、手牌 [{CardText.Format(hand)}] 时不一致："
                + $"\n漏掉的合法牌：{string.Join(" | ", missing)}"
                + $"\n多出来的非法牌：{string.Join(" | ", extra)}");
        }
    }

    [Fact]
    public void 生成器给出的每一手都一定是合法的()
    {
        // 上面那条测的是小局面下的"完全一致"，这条覆盖更大的手牌，只保证"不会给出非法牌"。
        for (var seed = 1; seed <= 20; seed++)
        {
            var hand = Hand(25, seed);
            var leadSource = Hand(25, seed + 500);

            foreach (var lead in MoveGenerator.GenerateLeads(leadSource, Trump))
            {
                foreach (var candidate in MoveGenerator.GenerateFollows(lead, hand, Trump))
                {
                    var result = MoveValidator.Validate(lead, hand, candidate.Cards, Trump);
                    Assert.True(result.IsValid, $"生成了非法牌 {candidate.Describe()}：{result.Reason}");
                }
            }
        }
    }

    [Fact]
    public void 领出时单张对子连对都能生成出来()
    {
        var hand = TestCards.Parse("♣7 ♣7 ♣8 ♣8 ♣9 ♣9 ♥K ♠2 大王").ToImmutableArray();
        var leads = MoveGenerator.GenerateLeads(hand, Trump);

        Assert.Contains(leads, l => l.Type == ComboType.Single);
        Assert.Contains(leads, l => l.Type == ComboType.Pair);
        Assert.Contains(leads, l => l.Type == ComboType.Tractor && l.PairCount == 2);

        // 三连对也要在：本版支持任意长度的连对。
        Assert.Contains(
            leads,
            l => l.Type == ComboType.Tractor && l.PairCount == 3 && l.Group == Suit.Club);

        // 生成的每一手都得能过领出校验。
        Assert.All(leads, l => Assert.True(MoveValidator.ValidateLead(l).IsValid));
    }

    [Fact]
    public void 领出不会给出重复的牌型()
    {
        var hand = TestCards.Parse("♣7 ♣7 ♣8 ♣8 ♥K ♥K ♠2 ♠2").ToImmutableArray();
        var leads = MoveGenerator.GenerateLeads(hand, Trump);

        var keys = leads.Select(l => CardSet.ShapeKey(l.Cards)).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}
