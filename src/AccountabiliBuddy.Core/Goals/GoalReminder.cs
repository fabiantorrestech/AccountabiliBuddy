using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal reminder (brief §5.1, §5.9): a suggestion that rings through the task reminder engine; nothing locks or waits.
/// A shared item of ADR 48/72 that travels in the sealed sync files (ADR 79, part <c>goalReminders</c>), newest version wins
/// (<see cref="GoalSyncMerge"/>); deleting its goal leaves it alone (hidden while the goal is gone). Never exported to Google Calendar.
/// </summary>
/// <param name="Weekdays">For <see cref="GoalReminderDays.Weekdays"/>: the days it rings on, by name; null otherwise.</param>
/// <param name="Date">For <see cref="GoalReminderDays.Date"/> and <see cref="GoalReminderDays.Today"/>: the date; null otherwise.</param>
/// <param name="Time">The local time it rings.</param>
/// <param name="Repeat">Whether it repeats on its days (false: it rings once, the next time it is due).</param>
/// <param name="Reason">Why, in the user's words. Private text: sealed at rest with the device's key, never logged; in a backup only inside a password file.</param>
/// <param name="SkipWhenDone">Checked at ring time: no ring when the goal's day (or period) is already hit.</param>
/// <param name="NudgeWhenBehind">Rings once, the morning after the goal drops below pace (on by default in AccountabiliBuddy mode, §5.10).</param>
public sealed record GoalReminder(
    Guid Id,
    Guid GoalId,
    GoalReminderDays Days,
    TimeOnly Time,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GoalWeekday>? Weekdays = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? Date = null,
    bool Repeat = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    bool SkipWhenDone = true,
    bool NudgeWhenBehind = false,
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
}
