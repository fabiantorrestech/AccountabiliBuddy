using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Loads and saves <see cref="TaskReminderSettings"/> (document "tasks.reminderSettings", this PC only, not per mode). A reminder
/// setting loosens nothing about a focus session (it only decides how a reminder rings), so every change applies at once. Use from the
/// UI thread only.
/// </summary>
public sealed class TaskReminderSettingsService
{
    public const string DocumentKey = "tasks.reminderSettings";

    private readonly SqliteJsonDocumentStore _store;

    public TaskReminderSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public TaskReminderSettings Current { get; private set; } = TaskReminderSettings.Default;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            TaskReminderSettings? loaded = Task.Run(() => _store.LoadAsync<TaskReminderSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null ? TaskReminderSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = TaskReminderSettings.Default;
        }
    }

    public async Task UpdateAsync(Func<TaskReminderSettings, TaskReminderSettings> change)
    {
        TaskReminderSettings changed = Clamped(change(Current));
        if (changed == Current)
        {
            return;
        }

        await _store.SaveAsync(DocumentKey, changed);
        Current = changed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the old device-local picked calendar once the account took it over.</summary>
    public Task ClearLegacyPickedCalendarAsync()
    {
        return UpdateAsync(current => current with { PickedCalendarId = null });
    }

    private static TaskReminderSettings Clamped(TaskReminderSettings settings)
    {
        string[] rules =
        [
            TaskReminderSettings.NoNewReminder, TaskReminder.AtStart, TaskReminder.TenMinutesBefore, TaskReminder.OneHourBefore, TaskReminder.DayBeforeAtNine,
        ];
        return settings with
        {
            Volume = Math.Clamp(settings.Volume, TaskReminderRingPolicy.MinVolume, 100),
            DismissProtectSeconds = Nearest(TaskReminderRingPolicy.ProtectChoices, settings.DismissProtectSeconds),
            StopAfterMinutes = Nearest(TaskReminderRingPolicy.StopAfterChoices, settings.StopAfterMinutes),
            SnoozeShortcuts = settings.SnoozeShortcuts is { Count: 3 } ? settings.SnoozeShortcuts : null,
            NewReminderRule = rules.Contains(settings.NewReminderRule) ? settings.NewReminderRule : TaskReminder.AtStart,
            NewRemindersTo = Enum.IsDefined(settings.NewRemindersTo) ? settings.NewRemindersTo : TaskCalendarDestination.GoogleCalendar,
            PickedCalendarId = string.IsNullOrWhiteSpace(settings.PickedCalendarId) ? null : settings.PickedCalendarId,
            DateOnlyEvent = Enum.IsDefined(settings.DateOnlyEvent) ? settings.DateOnlyEvent : TaskDateOnlyEvent.TimedAtNine,
        };
    }

    private static int Nearest(IReadOnlyList<int> choices, int value)
    {
        return choices.OrderBy(choice => Math.Abs(choice - value)).First();
    }
}
