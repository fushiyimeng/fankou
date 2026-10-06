using System.Collections.Immutable;
using FanKou.Core.Cards;
using FanKou.Core.Combinations;
using FanKou.Core.Game;
using FanKou.Core.Play;
using FanKou.Core.Trump;

namespace FanKou.Ai;

/// <summary>
/// 记牌。人打牌的时候都记得住已经出过什么，AI 不该比人傻。
///
/// 这里管的是"哪些牌还没露过面"：露过面的只有三种——自己手里的、别人打过的、叫主亮过的。
/// 剩下的既可能在别人手里，也可能在还没翻开的锅底里，**AI 分不出来也不该分得出来**。
/// </summary>
public sealed class CardMemory
{
    private readonly HashSet<int> _appeared;
    private readonly List<Card> _unseen;
    private readonly Dictionary<(Suit Suit, Rank Rank), int> _unseenShape = [];

    private CardMemory(HashSet<int> appeared, List<Card> unseen)
    {
        _appeared = appeared;
        _unseen = unseen;

        foreach (var card in unseen)
        {
            var key = (card.Suit, card.Rank);
            _unseenShape[key] = _unseenShape.GetValueOrDefault(key) + 1;
        }

        UnseenPoints = unseen.Sum(c => c.Points);
    }

    /// <summary>还没露过面的牌（可能在某家手上，也可能在锅底）。</summary>
    public IReadOnlyList<Card> Unseen => _unseen;

    public int UnseenCount => _unseen.Count;

    /// <summary>还没露面的分牌一共值多少分。</summary>
    public int UnseenPoints { get; }

    public static CardMemory Build(IReadOnlyList<Card> ownHand, PublicBoardInfo info)
    {
        var appeared = new HashSet<int>();

        foreach (var card in ownHand)
        {
            appeared.Add(card.Id);
        }

        foreach (var card in info.SeenCards())
        {
            appeared.Add(card.Id);
        }

        var unseen = Deck.Create().Where(c => !appeared.Contains(c.Id)).ToList();
        return new CardMemory(appeared, unseen);
    }

    /// <summary>这张牌露过面没有（自己手上的也算露过）。</summary>
    public bool HasAppeared(Card card) => _appeared.Contains(card.Id);

    /// <summary>还有几张主牌没露面。</summary>
    public int UnseenTrumps(TrumpContext trump) =>
        _unseen.Count(c => IdentityResolver.Resolve(c, trump).IsTrump);

    /// <summary>
    /// 外面还有几手牌能压住这一手。
    /// 领出的时候把 <paramref name="lead"/> 传成它自己，问的就是"我领出去会不会被压"。
    /// 张数一样的才会被数进来；连对按同花色连续对子算。
    /// </summary>
    /// <param name="countUnseenTrumps">
    /// 还没露面的主牌算不算威胁。只有"已证实两家对头都没主了"的时候才敢传 false——
    /// 那时候外面剩的主只可能在队友手上或者锅里，压不到我。
    /// </param>
    public int UnseenBeating(
        CardCombination combo,
        CardCombination lead,
        TrumpContext trump,
        bool countUnseenTrumps = true)
    {
        var count = 0;

        foreach (var candidate in PlausibleSameLength(combo.Length, trump))
        {
            if (!countUnseenTrumps
                && candidate.Cards.All(c => IdentityResolver.Resolve(c, trump).IsTrump))
            {
                continue;
            }

            if (TrickEvaluator.BeatsInContext(candidate, combo, lead))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>把还没露面的牌拼成"张数一样、形状说得通"的候选，用来数外面有几手能压。</summary>
    private IEnumerable<CardCombination> PlausibleSameLength(int length, TrumpContext trump)
    {
        if (length == 1)
        {
            foreach (var card in _unseen)
            {
                if (ComboRecognizer.TryRecognize([card], trump, out var single))
                {
                    yield return single!;
                }
            }

            yield break;
        }

        if (length == 2)
        {
            foreach (var (key, count) in _unseenShape)
            {
                if (count < 2 || key.Suit == Suit.Joker)
                {
                    continue;
                }

                var pair = new[]
                {
                    new Card(key.Suit, key.Rank, 0),
                    new Card(key.Suit, key.Rank, 1),
                };

                if (ComboRecognizer.TryRecognize(pair, trump, out var recognized))
                {
                    yield return recognized!;
                }
            }

            // 王对（两张小王 / 两张大王）
            foreach (var rank in new[] { Rank.SmallJoker, Rank.BigJoker })
            {
                if (_unseenShape.GetValueOrDefault((Suit.Joker, rank)) < 2)
                {
                    continue;
                }

                var pair = new[]
                {
                    new Card(Suit.Joker, rank, 0),
                    new Card(Suit.Joker, rank, 1),
                };

                if (ComboRecognizer.TryRecognize(pair, trump, out var recognized))
                {
                    yield return recognized!;
                }
            }

            yield break;
        }

        // 连对：按花色找同一门里能连起来的对子，取够长的连续段
        var needPairs = length / 2;
        if (needPairs < 2 || length % 2 != 0)
        {
            yield break;
        }

        foreach (var suit in Suits.All.Append(Suit.Joker))
        {
            var pairs = _unseenShape
                .Where(kv => kv.Key.Suit == suit && kv.Value >= 2)
                .Select(kv => (kv.Key.Rank, Order: TractorOrder(kv.Key, trump)))
                .OrderBy(x => x.Order)
                .ToArray();

            for (var start = 0; start + needPairs <= pairs.Length; start++)
            {
                var consecutive = true;
                for (var i = start; i + 1 < start + needPairs; i++)
                {
                    if (pairs[i + 1].Order != pairs[i].Order + 1)
                    {
                        consecutive = false;
                        break;
                    }
                }

                if (!consecutive)
                {
                    continue;
                }

                var cards = ImmutableArray.CreateBuilder<Card>();
                for (var i = start; i < start + needPairs; i++)
                {
                    cards.Add(new Card(suit, pairs[i].Rank, 0));
                    cards.Add(new Card(suit, pairs[i].Rank, 1));
                }

                if (ComboRecognizer.TryRecognize(cards, trump, out var tractor))
                {
                    yield return tractor!;
                }
            }
        }
    }

    private static int TractorOrder((Suit Suit, Rank Rank) key, TrumpContext trump) =>
        IdentityResolver.Resolve(new Card(key.Suit, key.Rank, 0), trump).TractorRank;
}
