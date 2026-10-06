using System.Collections.Immutable;
using FanKou.Core.Util;

namespace FanKou.Core.Cards;

/// <summary>两副完整正牌（含大小王），共 108 张。</summary>
public static class Deck
{
    public const int Decks = 2;
    public const int CardsPerDeck = 54;
    public const int TotalCards = Decks * CardsPerDeck; // 108

    /// <summary>按固定顺序构造，顺序固定是为了测试可复现；正式开局一律先洗牌。</summary>
    public static ImmutableArray<Card> Create()
    {
        var builder = ImmutableArray.CreateBuilder<Card>(TotalCards);
        for (byte copy = 0; copy < Decks; copy++)
        {
            foreach (var suit in Suits.All)
            {
                foreach (var rank in Ranks.Standard)
                {
                    builder.Add(new Card(suit, rank, copy));
                }
            }

            builder.Add(new Card(Suit.Joker, Rank.SmallJoker, copy));
            builder.Add(new Card(Suit.Joker, Rank.BigJoker, copy));
        }

        return builder.MoveToImmutable();
    }

    public static ImmutableArray<Card> CreateShuffled(IRandomSource rng) => Shuffle(Create(), rng);

    /// <summary>Fisher-Yates，用注入的随机源以保证同 seed 可复现。</summary>
    public static ImmutableArray<Card> Shuffle(ImmutableArray<Card> cards, IRandomSource rng)
    {
        var array = cards.ToArray();
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }

        return [.. array];
    }
}
