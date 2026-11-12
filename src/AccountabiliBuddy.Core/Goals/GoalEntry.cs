using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One value for a goal on a day (brief §5.1): a hand tick or log, time added by hand, or a reading (Health Connect, screen
/// time, Beeminder, calendar). Every entry is kept. How a day's value comes from them depends on the goal's kind
/// (<see cref="GoalCounting"/>): for an Amount goal the latest hand edit wins (over any later reading or sync), else the day's
/// first reading. Carries the shared-item stamps of ADR 48/72 for a later ADR 79 merge; nothing syncs it now.
/// </summary>
/// <param name="Day">The local date it is for (a past date when back-filled).</param>
/// <param name="Time">
/// The local time of the reading, or when time added by hand started (a Time goal's hand time with a start counts once with
/// focus that overlaps it); null = no time (added on top).
/// </param>
/// <param name="Value">In the goal's unit: minutes, a count, lb or kg, steps; a habit's tick is 1, an explicit untick 0.</param>
/// <param name="EditedAt">When it was made or last edited: the item stamp, and what "latest hand edit" means.</param>
/// <param name="Position">Its place in the day's list (ADR 48 items carry one); 0 when unordered.</param>
/// <param name="DeletedAt">Set on a tombstone (an untick or a removed log).</param>
public sealed record GoalEntry(
    Guid Id,
    Guid GoalId,
    DateOnly Day,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TimeOnly? Time,
    double Value,
    GoalEntrySource Source,
    DateTimeOffset EditedAt,
    double Position = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>Whether it was made after its day (a back-fill, change note §4a): it updates that week and the streak, never a guard.</summary>
    public bool IsBackFill(TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(EditedAt, zone).DateTime) > Day;
    }
}
