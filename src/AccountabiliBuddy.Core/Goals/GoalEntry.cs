using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One value for a goal on a day (brief §5.1): a hand tick or log, time added by hand, or a reading (Health Connect, screen
/// time, Beeminder, calendar). Every entry is kept. How a day's value comes from them depends on the goal's kind
/// (<see cref="GoalCounting"/>): for an Amount goal the latest hand edit wins (over any later reading or sync), else the day's
/// first reading. A shared item of ADR 48/72 that travels in the sealed sync files (ADR 79, part <c>goalEntries</c>), newest version
/// wins (<see cref="GoalSyncMerge"/>); deleting its goal leaves it alone (hidden while the goal is gone, dropped with the goal's tombstone).
/// </summary>
/// <param name="Day">The local date it is for (a past date when back-filled).</param>
/// <param name="Time">
/// The local time of the reading, or when time added by hand started (a Time goal's hand time with a start counts once with
/// focus that overlaps it); null = no time (added on top).
/// </param>
/// <param name="Value">
/// In the goal's unit: minutes, a count, lb or kg, steps. A habit's tick is 1; <b>"Untick" is always a new live hand entry with
/// value 0</b>, which as the latest hand entry wins over the threshold (the day reads Missed). Never a tombstone.
/// </param>
/// <param name="EditedAt">When it was made or last edited: the item stamp, and what "latest hand edit" means.</param>
/// <param name="Position">Its place in the day's list (ADR 48 items carry one); 0 when unordered.</param>
/// <param name="DeletedAt">
/// Set on a tombstone: a removed log (Delete in the day panel, or the goal deleted). The day goes back to what its other entries
/// and sources say; for a habit that may be ticking itself again. Never used to untick.
/// </param>
public sealed record GoalEntry(
    Guid Id,
    Guid GoalId,
    DateOnly Day,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TimeOnly? Time,
    double Value,
    GoalEntrySource Source,
    DateTimeOffset EditedAt,
    double Position = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null) : ISharedItem
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>The item as one text, for the rare tie of two versions with the same stamp (<see cref="SharedListMerge"/>). Never logged or shown.</summary>
    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>Whether it was made after its day (a back-fill, change note §4a): it updates that week and the streak, never a guard.</summary>
    public bool IsBackFill(TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(EditedAt, zone).DateTime) > Day;
    }
}
