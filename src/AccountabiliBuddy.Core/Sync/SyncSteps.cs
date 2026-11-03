using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A phone's daily step counts as its sync file carries them: the goal, when Health Connect was last read and the counts of the
/// last few days. Numbers and dates only. Written only by the phone that read them, and only while "Track daily steps" is on;
/// they go to the user's own devices and nowhere else, and the watch doesn't get them. Read leniently by
/// <see cref="SyncStepsConverter"/>, so a wrong type never makes the whole file unreadable.
/// </summary>
/// <param name="Goal">The daily goal in steps.</param>
/// <param name="ReadAt">When Health Connect was read (the "updated" time); the PC takes the newest of the phones' files.</param>
/// <param name="Days">At most <see cref="MaxDays"/> days, oldest first; a day with no data is left out, never written as 0.</param>
[JsonConverter(typeof(SyncStepsConverter))]
public sealed record SyncSteps(int Goal, DateTimeOffset ReadAt, IReadOnlyList<SyncStepDay> Days)
{
    public const int MaxDays = 14;
    public const int MaxGoal = 100_000;
}
