using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal reminder (brief §5.1, §5.9): a suggestion that rings through the task reminder engine; nothing locks or waits.
/// Carries the shared-item stamps of ADR 48/72 for a later ADR 79 merge; nothing syncs it now. Never exported to Google Calendar.
/// </summary>
/// <param name="Weekdays">For <see cref="GoalReminderDays.Weekdays"/>: the days it rings on; null otherwise.</param>
/// <param name="Date">For <see cref="GoalReminderDays.Date"/> and <see cref="GoalReminderDays.Today"/>: the date; null otherwise.</param>
/// <param name="Time">The local time it rings.</param>
/// <param name="Repeat">Whether it repeats on its days (false: it rings once, the next time it is due).</param>
/// <param name="Reason">Why, in the user's words. Private text: sealed at rest, never logged.</param>
/// <param name="SkipWhenDone">Checked at ring time: no ring when the goal's day (or period) is already hit.</param>
/// <param name="NudgeWhenBehind">Rings once, the morning after the goal drops below pace (on by default in AccountabiliBuddy mode, §5.10).</param>
public sealed record GoalReminder(
    Guid Id,
    Guid GoalId,
    GoalReminderDays Days,
    TimeOnly Time,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DayOfWeek>? Weekdays = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? Date = null,
    bool Repeat = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    bool SkipWhenDone = true,
    bool NudgeWhenBehind = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    [JsonIgnore]
    public bool IsLive => DeletedAt is null;
}
