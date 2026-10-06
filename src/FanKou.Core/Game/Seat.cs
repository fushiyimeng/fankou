namespace FanKou.Core.Game;

/// <summary>四个座位。出牌顺序就是按这个枚举的值顺时针走：东 → 南 → 西 → 北。</summary>
public enum Seat : byte
{
    East = 0,
    South = 1,
    West = 2,
    North = 3,
}

/// <summary>两支队。对家是一伙的：东西一队，南北一队。</summary>
public enum Team : byte
{
    EastWest = 0,
    NorthSouth = 1,
}

public static class Seats
{
    public static readonly Seat[] All = [Seat.East, Seat.South, Seat.West, Seat.North];

    /// <summary>东=东西队、南=南北队、西=东西队、北=南北队，恰好交替。</summary>
    public static Team TeamOf(this Seat seat) => (Team)((int)seat % 2);

    /// <summary>对家。</summary>
    public static Seat Partner(this Seat seat) => (Seat)(((int)seat + 2) % 4);

    /// <summary>
    /// **出牌是逆时针**：东 → 北 → 西 → 南。
    /// 屏幕上东家在右、南家在下，所以这是逆时针转。
    /// </summary>
    public static Seat NextInPlay(this Seat seat) => (Seat)(((int)seat + 3) % 4);

    /// <summary>
    /// **发牌是顺时针**：东 → 南 → 西 → 北。
    /// 注意跟出牌方向是反的。
    /// </summary>
    public static Seat NextInDeal(this Seat seat) => (Seat)(((int)seat + 1) % 4);

    /// <summary>按出牌顺序（逆时针）从某个座位开始的四家。</summary>
    public static IEnumerable<Seat> InPlayOrderFrom(Seat start)
    {
        var seat = start;
        for (var i = 0; i < 4; i++)
        {
            yield return seat;
            seat = seat.NextInPlay();
        }
    }

    /// <summary>按发牌顺序（顺时针）从某个座位开始的四家。</summary>
    public static IEnumerable<Seat> InDealOrderFrom(Seat start)
    {
        var seat = start;
        for (var i = 0; i < 4; i++)
        {
            yield return seat;
            seat = seat.NextInDeal();
        }
    }

    public static Team Other(this Team team) => (Team)(1 - (int)team);

    public static Seat[] Members(this Team team) => [(Seat)(int)team, (Seat)((int)team + 2)];

    public static string Name(this Seat seat) => seat switch
    {
        Seat.East => "东",
        Seat.South => "南",
        Seat.West => "西",
        Seat.North => "北",
        _ => "?",
    };

    public static string Name(this Team team) => team == Team.EastWest ? "东西" : "南北";

    public static bool IsSameTeam(this Seat a, Seat b) => a.TeamOf() == b.TeamOf();
}
