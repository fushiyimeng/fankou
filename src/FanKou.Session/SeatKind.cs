namespace FanKou.Session;

/// <summary>
/// 一个座位由谁来打。桌面版是"人类固定一家、其余全是 AI"，
/// 网页版和以后的联机版需要能分辨"本机真人 / AI / 远端真人"。
/// </summary>
public enum SeatKind
{
    /// <summary>本机玩家：轮到他时停下等他操作。</summary>
    LocalHuman = 0,

    /// <summary>电脑玩家：轮到他就按座位上的脑子自己出。</summary>
    Ai = 1,

    /// <summary>远端真人：现在还没有联机，暂时跟 <see cref="Ai"/> 一样处理。</summary>
    RemoteHuman = 2,
}
