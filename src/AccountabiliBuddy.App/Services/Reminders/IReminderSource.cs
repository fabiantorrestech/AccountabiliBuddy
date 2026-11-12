using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Reminders;

/// <summary>
/// Reminders that ring through the task reminder engine without being tasks (round 2 R5.5, brief §5.9; goal reminders today): the
/// source says which of its occurrences exist and where each stands, and keeps what the user does to them (snooze, dismiss, a ring that
/// ran out) in its own store. <see cref="Tasks.TaskReminderService"/> merges the due ones after the task list's own, through the same
/// due rule, session rule ("During a focus session"), snooze menu and timer; <see cref="Tasks.TaskReminderRinger"/> rings them with the
/// same cards, bell and volume guard. A source's reminders are suggestions: never Can't miss, never full screen, and nothing is written
/// to a task, exported to Google Calendar (ADR 78 is task-only) or synced. Use from the UI thread only.
/// </summary>
public interface IReminderSource
{
    /// <summary>The origin its entries carry (<see cref="TaskReminderEntry.Origin"/>); one source per origin.</summary>
    TaskReminderOrigin Origin { get; }

    /// <summary>Something its occurrences depend on changed (its items, its stored state, the data a rule reads, the mode).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Every occurrence it knows of now, handled or not, each with <see cref="TaskReminderEntry.Origin"/> set and never Can't miss. The
    /// engine picks the due ones (<see cref="TaskReminderQueue.Due"/>) and the next ring time from them. Rules checked at ring time (such
    /// as "Skip when the goal is already done") are applied here.
    /// </summary>
    IReadOnlyList<TaskReminderEntry> Entries(DateTimeOffset now);

    /// <summary>Rings it again at <paramref name="until"/> (the engine's snooze menu and picker).</summary>
    Task SnoozeAsync(TaskReminderEntry entry, DateTimeOffset until);

    /// <summary>It doesn't ring again.</summary>
    Task DismissAsync(TaskReminderEntry entry);

    /// <summary>Its ring time ran out with nobody acting: it doesn't ring again (at once in <see cref="Entries"/>; the save may follow).</summary>
    Task RingTimedOutAsync(TaskReminderEntry entry);

    /// <summary>Rings once, quietly (one bell, no volume raise), like a reminder during a session (ProductaBuddy's plain notification).</summary>
    bool RingsQuietly(TaskReminderEntry entry);

    /// <summary>What its card shows; null when its item is gone.</summary>
    ReminderSourceCard? Card(TaskReminderEntry entry);

    /// <summary>The card's accent button (<see cref="ReminderSourceCard.PrimaryText"/>); the occurrence counts as handled afterwards.</summary>
    Task PrimaryAsync(TaskReminderEntry entry);

    /// <summary>The card's "Open" (its item on its own page); the occurrence counts as handled afterwards.</summary>
    Task OpenAsync(TaskReminderEntry entry);
}
