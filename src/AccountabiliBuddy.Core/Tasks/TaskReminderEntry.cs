using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One occurrence of a task's reminder as it stands (ADR 76): which task and reminder, when it was due, what happened to it, and
/// when it rings next. Holds no task text, so it is safe to log by its ids and times.
/// </summary>
/// <param name="Due">When the occurrence was due (its identity: a changed due date makes a new, fresh occurrence).</param>
/// <param name="RingAt">When it rings: <paramref name="Due"/>, or the end of a snooze.</param>
public sealed record TaskReminderEntry(
    Guid TaskId,
    Guid ReminderId,
    string Rule,
    DateTimeOffset Due,
    TaskReminderStatus Status,
    DateTimeOffset RingAt,
    int SnoozeCount,
    bool CantMiss)
{
    /// <summary>Dismissed or started (on any device): it does not ring again.</summary>
    public bool Handled => Status is TaskReminderStatus.Dismissed or TaskReminderStatus.Started;

    /// <summary>Which occurrence this is, for remembering it (never shown).</summary>
    public string Key => $"{ReminderId:N}:{Due.UtcTicks}:{RingAt.UtcTicks}";
}
