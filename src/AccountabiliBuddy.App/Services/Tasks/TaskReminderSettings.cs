using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// How this PC rings task reminders (document "tasks.reminderSettings", device-local, the same in both modes; ADR 76, spec 05 §7).
/// Settings › Alerts &amp; on-screen › Task reminders shows them.
/// </summary>
/// <param name="Volume">Reminder volume, 10 to 100 (shared with the phone's setting by value, not synced).</param>
/// <param name="RisingVolume">Rises over 30 s (on by default).</param>
/// <param name="RingEvenWhenMuted">Raises or unmutes the output while ringing, then puts the user's level back (off by default).</param>
/// <param name="DismissProtect">Dismiss waits <paramref name="DismissProtectSeconds"/> before it works (on, 5 s; 3, 5 or 10).</param>
/// <param name="StopAfterMinutes">A ring stops by itself after this long (1, 5, 10 or 30; default 10).</param>
/// <param name="Display">Toast card (default) or always full screen.</param>
/// <param name="SnoozeShortcuts">The three snooze shortcuts; null for the defaults (Tonight 6:00 PM, Tomorrow 9:00 AM, Monday 9:00 AM).</param>
/// <param name="DuringSession">"During a focus session": normal reminders wait until it ends (default) or remind quietly; Can't miss always rings.</param>
/// <param name="NewReminderRule">
/// "New reminders start at" (Settings › Tasks › Reminders): a <see cref="TaskReminder"/> rule ("At start time" by default), or
/// <see cref="NoNewReminder"/> for None.
/// </param>
/// <param name="NewRemindersTo">"Add new reminders to" (ADR 78): the per-task calendar switch's default when a task gets its first reminder or due date.</param>
/// <param name="PickedCalendarId">
/// Before the setting became the account's (<see cref="TaskAccountSettingsService.PickedCalendarId"/>): ADR 78's picked calendar on this
/// PC. Read once to take it over (<see cref="TaskAccountSettingsService.AdoptLegacyAsync"/>), then cleared; never written again.
/// </param>
/// <param name="DateOnlyEvent">"Tasks with a date but no time" (ADR 78): a timed event at 9:00 AM (default) or an all-day event.</param>
/// <param name="PendingDuringSession">
/// "Remind quietly" waiting to take effect: showing reminders during a focus session is the looser direction, so it waits the
/// looser-change wait and takes effect only while no session runs (<see cref="TaskReminderSettingsService.SetDuringSessionAsync"/>).
/// </param>
/// <param name="PendingDuringSessionFrom">When <paramref name="PendingDuringSession"/> may take effect (trusted clock).</param>
public sealed record TaskReminderSettings(
    int Volume = 70,
    bool RisingVolume = true,
    bool RingEvenWhenMuted = false,
    bool DismissProtect = true,
    int DismissProtectSeconds = 5,
    int StopAfterMinutes = 10,
    TaskReminderDisplay Display = TaskReminderDisplay.Toast,
    IReadOnlyList<TaskSnoozeShortcut>? SnoozeShortcuts = null,
    string NewReminderRule = TaskReminder.AtStart,
    TaskReminderDuringSession DuringSession = TaskReminderDuringSession.WaitUntilEnd,
    TaskCalendarDestination NewRemindersTo = TaskCalendarDestination.GoogleCalendar,
    string? PickedCalendarId = null,
    TaskDateOnlyEvent DateOnlyEvent = TaskDateOnlyEvent.TimedAtNine,
    TaskReminderDuringSession? PendingDuringSession = null,
    DateTimeOffset? PendingDuringSessionFrom = null)
{
    /// <summary>"New reminders start at: None": tasks have no reminder until one is added.</summary>
    public const string NoNewReminder = "none";

    public static TaskReminderSettings Default { get; } = new();

    /// <summary>The ring behaviour these settings give.</summary>
    public TaskReminderRingPolicy Policy => new(Volume, RisingVolume, DismissProtect, DismissProtectSeconds, StopAfterMinutes);

    /// <summary>The three shortcuts to show (always three: a missing or wrong-sized list gives the defaults).</summary>
    public IReadOnlyList<TaskSnoozeShortcut> Shortcuts => SnoozeShortcuts is { Count: 3 } own ? own : TaskSnooze.DefaultShortcuts;
}
