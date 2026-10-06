using System.Collections.Immutable;

namespace FanKou.Core.Game;

/// <summary>
/// 打完的一轮牌的记录。给 AI 记牌用——已经出过哪些牌是公开信息，
/// 人打牌的时候也都记得住，AI 不该比人傻。
/// </summary>
public sealed record TrickRecord(
    int TrickNumber,
    Seat Winner,
    ImmutableArray<TrickPlay> Plays,
    int Points,
    bool DefenderWon)
{
    /// <summary>这一轮领出的那一手。</summary>
    public TrickPlay Lead => Plays[0];
}
