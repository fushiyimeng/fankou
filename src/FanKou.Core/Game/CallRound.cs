namespace FanKou.Core.Game;

/// <summary>
/// 现在轮到哪一圈人叫主：首局（无庄，谁都还没定）、庄家那一队、还是闲家那一队。
/// 文档第 42、43 行：庄家先来，庄家有牌就必须叫，无牌才轮到闲家。
/// </summary>
public enum CallRound
{
    FirstRound,
    DeclarerTeam,
    DefenderTeam,
}
