using FanKou.Core.Cards;
using FanKou.Core.Levels;

namespace FanKou.Core.Tests;

public class LevelTrackTests
{
    [Fact]
    public void 序列共16步且5_10_K各出现两次()
    {
        // 规则文档第三次补充第 1 条：5 → 10 → K → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → J → Q → K → A
        Assert.Equal(16, LevelTrack.Count);
        Assert.Equal(16, LevelTrack.Sequence.Length);

        Assert.Equal(Rank.Five, LevelTrack.Sequence[0]);
        Assert.Equal(Rank.Five, LevelTrack.Sequence[6]);
        Assert.Equal(Rank.Ten, LevelTrack.Sequence[1]);
        Assert.Equal(Rank.Ten, LevelTrack.Sequence[11]);
        Assert.Equal(Rank.King, LevelTrack.Sequence[2]);
        Assert.Equal(Rank.King, LevelTrack.Sequence[14]);

        Assert.Equal(Rank.Ace, LevelTrack.Sequence[15]);
    }

    [Fact]
    public void 两次出现的同名等级是不同下标()
    {
        // 这正是不能拿 Rank 当等级存的原因：存 Rank 的话这两步就合并成一步了。
        Assert.NotEqual(0, 6);
        Assert.Equal(LevelTrack.RankAt(0), LevelTrack.RankAt(6));

        Assert.Equal(Rank.Two, LevelTrack.RankAt(3));
        Assert.Equal(Rank.Three, LevelTrack.RankAt(4));
        Assert.Equal(Rank.Four, LevelTrack.RankAt(5));
        Assert.Equal(Rank.Six, LevelTrack.RankAt(7));
        Assert.Equal(Rank.Ace, LevelTrack.RankAt(15));
    }

    [Theory]
    [InlineData(0)]  // 5
    [InlineData(1)]  // 10
    [InlineData(2)]  // K
    public void 未过2不保级(int index)
    {
        Assert.False(LevelTrack.IsProtected(index));
        Assert.Equal(0, LevelTrack.Demote(index));
    }

    [Theory]
    [InlineData(3)]  // 2，刚到保级线
    [InlineData(7)]
    [InlineData(15)] // A
    public void 过了2之后掉级无效(int index)
    {
        Assert.True(LevelTrack.IsProtected(index));
        Assert.Equal(index, LevelTrack.Demote(index));
    }

    [Fact]
    public void 晋级按序列推进而不是按Rank()
    {
        // 文档第 5 行："升一级就是 10，再升一级是 K，翻过了 K 进入 2"。
        Assert.Equal(1, LevelTrack.Advance(0, 1));          // 5   → 10
        Assert.Equal(2, LevelTrack.Advance(1, 1));          // 10  → K
        Assert.Equal(3, LevelTrack.Advance(2, 1));          // K   → 2
        Assert.Equal(Rank.Two, LevelTrack.RankAt(LevelTrack.Advance(2, 1)));

        // 文档第 29 行：闲家 200 分庄家升两级。
        Assert.Equal(4, LevelTrack.Advance(2, 2));          // K   → 3（跳过 2 之后的下一级）
        Assert.Equal(Rank.Three, LevelTrack.RankAt(LevelTrack.Advance(2, 2)));
    }

    [Fact]
    public void 打到A就封顶不会溢出()
    {
        Assert.Equal(LevelTrack.AceIndex, LevelTrack.Advance(LevelTrack.AceIndex, 2));
        Assert.True(LevelTrack.IsAce(LevelTrack.AceIndex));
        Assert.False(LevelTrack.IsAce(LevelTrack.AceIndex - 1));
    }
}
