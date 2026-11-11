using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>The state of one occurrence of a <see cref="TaskReminder"/> (ADR 76).</summary>
/// <param name="Due">When this occurrence rings (before any snooze).</param>
/// <param name="SnoozedUntil">When a snoozed occurrence rings again; null otherwise.</param>
/// <param name="SnoozeCount">How many times it was snoozed (3 or more shows the "Put it in Queued?" nudge).</param>
public sealed record TaskReminderOccurrence(
    DateTimeOffset Due,
    TaskReminderStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SnoozedUntil = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int SnoozeCount = 0);
