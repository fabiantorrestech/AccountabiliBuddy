using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One of a task's reminders (ADR 76): at most <see cref="MaxPerTask"/>, Google Calendar's limit for one event's overrides.
/// </summary>
/// <param name="Rule">
/// One of <see cref="AtStart"/>, <see cref="TenMinutesBefore"/>, <see cref="OneHourBefore"/>, <see cref="DayBeforeAtNine"/>
/// (relative to the task's due date and time) or <see cref="Absolute"/> (rings at <paramref name="At"/>). An unknown rule from a
/// newer app is kept and not rung.
/// </param>
/// <param name="At">The moment of an <see cref="Absolute"/> reminder; null for the relative rules.</param>
/// <param name="Occurrences">Per-occurrence state (up, snoozed, dismissed, started); null when none has rung yet.</param>
/// <param name="CalendarEventId">The calendar event carrying it, when exported; null otherwise.</param>
public sealed record TaskReminder(
    Guid Id,
    string Rule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? At = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TaskReminderOccurrence>? Occurrences = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CalendarEventId = null)
{
    public const int MaxPerTask = 5;

    public const string AtStart = "atStart";
    public const string TenMinutesBefore = "10mBefore";
    public const string OneHourBefore = "1hBefore";
    public const string DayBeforeAtNine = "1dBefore9am";
    public const string Absolute = "at";
}
