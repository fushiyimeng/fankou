namespace FanKou.Core.Game;

public enum GamePhase
{
    NotStarted = 0,

    /// <summary>
    /// 发牌阶段。首局（以及以后的抢主局）是一张一张发的，因为**发牌途中就可以叫主**——
    /// 摸到小王加一张对应花色的级牌就能当场叫，这就是"抢先叫主"。
    /// </summary>
    Dealing,

    /// <summary>叫主阶段。</summary>
    Calling,

    /// <summary>
    /// 反主阶段。只有对手能反，规格必须比当前的高（叫主 &lt; 定主 &lt; 小无主 &lt; 大无主），
    /// 一直可以反到双大王为止（文档第 14、55 行）。
    /// </summary>
    CounterCalling,

    /// <summary>叫主的人拿起底牌换牌。</summary>
    BottomSwap,

    /// <summary>出牌阶段。</summary>
    Playing,

    /// <summary>一局打完，正在结算。</summary>
    Settling,

    /// <summary>本局结束，等下一局。</summary>
    RoundOver,

    /// <summary>房间结束（有人打到 A 又赢了）。</summary>
    RoomOver,
}
