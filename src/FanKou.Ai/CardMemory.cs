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
    /// <summary>锅底固定 8 张（文档第 21 行），从头到尾打不出来。</summary>
    private const int BottomSize = 8;

    private readonly HashSet<int> _appeared;
    private readonly List<Card> _unseen;
    private readonly Dictionary<(Suit Suit, Rank Rank), int> _unseenShape = [];

    /// <summary>
    /// 还没露面的牌里，每一张**真的在别人手上**的概率。
    ///
    /// 锅底那 8 张谁也打不出来，可它们混在"没露面"里，于是会被当成威胁——
    /// 这就是 AI 虚惊的来源。而且这个虚高**越到残局越离谱**：
    /// 开局 83 张没露面时只虚高约 10%，残局只剩十几张时能虚高一多半，
    /// 也就是**它恰恰在最该算准的时候最不准**。
    ///
    /// 庄家那边是例外：他看过锅底，那 8 张会在 <see cref="Build"/> 里直接剔掉，
    /// 此时这个系数就是 1，不需要打折。
    /// </summary>
    private readonly double _inHandFraction;

    private CardMemory(HashSet<int> appeared, List<Card> unseen, bool bottomKnown)
    {
        _appeared = appeared;
        _unseen = unseen;

        foreach (var card in unseen)
        {
            var key = (card.Suit, card.Rank);
            _unseenShape[key] = _unseenShape.GetValueOrDefault(key) + 1;
        }

        UnseenPoints = unseen.Sum(c => c.Points);

        _inHandFraction = bottomKnown || unseen.Count == 0
            ? 1
            : Math.Max(0, unseen.Count - BottomSize) / (double)unseen.Count;
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

        // 庄家知道锅底里是哪 8 张（他自己扣的）。这几张谁也打不出来，
        // 直接从"没露面的牌"里划掉——对庄家来说这是**精确**的，不用像别人那样折算。
        var bottomKnown = info.Bottom.Count > 0;

        foreach (var card in info.Bottom)
        {
            appeared.Add(card.Id);
        }

        var unseen = Deck.Create().Where(c => !appeared.Contains(c.Id)).ToList();
        return new CardMemory(appeared, unseen, bottomKnown);
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
        bool countUnseenTrumps = true) =>
        PlausibleBeating(combo, lead, trump, countUnseenTrumps).Count();

    /// <summary>
    /// 外面**预计**还有几手能压住这一手——比 <see cref="UnseenBeating"/> 多了锅底那一折。
    ///
    /// 一手牌要压住我，它要凑的每一张都得**在别人手上**；其中任何一张压在锅底，
    /// 这一手根本打不出来。所以按"每张真在手上的概率"折算。
    ///
    /// ⚠️ **目前在出牌决策上是个恒等变换，改不了任何决定。** 这不是估计，是算得出来的：
    /// 同一次决策里所有候选跟的是同一个领出、张数必然相同，所以权重 f^L 是一个**共同的常数**，
    /// 全体威胁值被同倍缩放，而下游只拿它们排序（<c>OrderBy</c>）和判零——
    /// 单调缩放既不改变顺序也不改变"是不是 0"，于是决策一模一样。
    /// 2026-10-06 实测印证过：1000 个种子配对，改前改后困难档对普通档都是 50.0%。
    ///
    /// 留着它是为了让**语义**是对的——将来真做搜索的时候，"外面预计几手能压"
    /// 要拿去算期望值，那时候值本身就有意义了，不能再拿含锅底的假数字去算。
    /// 在那之前别指望它提升强度。
    /// </summary>
    public double ExpectedBeating(
        CardCombination combo,
        CardCombination lead,
        TrumpContext trump,
        bool countUnseenTrumps = true)
    {
        var total = 0.0;

        foreach (var (_, weight) in PlausibleBeating(combo, lead, trump, countUnseenTrumps))
        {
            total += weight;
        }

        return total;
    }

    /// <summary>还没露面、且压得住这一手的牌型，附带"它真能被打出来"的概率权重。</summary>
    private IEnumerable<(CardCombination Combo, double Weight)> PlausibleBeating(
        CardCombination combo,
        CardCombination lead,
        TrumpContext trump,
        bool countUnseenTrumps)
    {
        foreach (var candidate in PlausibleSameLength(combo.Length, trump))
        {
            if (!countUnseenTrumps
                && candidate.Cards.All(c => IdentityResolver.Resolve(c, trump).IsTrump))
            {
                continue;
            }

            if (!TrickEvaluator.BeatsInContext(candidate, combo, lead))
            {
                continue;
            }

            yield return (candidate, Math.Pow(_inHandFraction, candidate.Length));
        }
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
