namespace AccountabiliBuddy.Core.Sync;

/// <summary>One kind of night's smart bedtime (ADR 34), as the phone's status line shows it.</summary>
/// <param name="LevelMinutes">Minutes in bed before waking: tonight's bedtime is the wake time minus this; null while learning.</param>
/// <param name="Nights">Measured nights of this kind in the last 14 days.</param>
/// <param name="LatencyMinutes">The usual time from putting the phone down to falling asleep.</param>
/// <param name="AverageAsleep">The average time of falling asleep, "HH:mm"; null with no nights.</param>
/// <param name="AverageSleptMinutes">The average sleep; null with no nights.</param>
/// <param name="MadeThisWeek">Nights this week the user made the bedtime asked.</param>
/// <param name="AskedThisWeek">Nights this week a bedtime was asked.</param>
public sealed record SyncSmartLevel(
    int? LevelMinutes, int Nights, int LatencyMinutes, string? AverageAsleep, int? AverageSleptMinutes, int MadeThisWeek, int AskedThisWeek);
