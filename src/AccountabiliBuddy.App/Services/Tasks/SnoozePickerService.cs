using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Tasks;
using Avalonia;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// "Pick a time…" for a ringing reminder: shows "Snooze until…" (<see cref="SnoozePickerDialog"/>) over the window that asked and
/// snoozes the reminder to the moment picked (<see cref="TaskReminderService.SnoozeAsync"/>). Snooze works at once, as everywhere; only
/// Dismiss waits. The reminder cards' "Pick a time…" (<see cref="TaskReminderRinger"/>) use it. Use from the UI thread.
/// </summary>
public sealed class SnoozePickerService
{
    private readonly TaskReminderService _reminders;
    private readonly TaskReminderSettingsService _settings;

    public SnoozePickerService(TaskReminderService reminders, TaskReminderSettingsService settings)
    {
        _reminders = reminders;
        _settings = settings;
    }

    /// <returns>True when the reminder was snoozed; false when the picker was cancelled.</returns>
    public async Task<bool> ShowAsync(Visual? owner, TaskReminderEntry entry)
    {
        var picker = new SnoozePickerViewModel(_settings, () => _reminders.Now, _reminders.Zone, _reminders.TitleOf(entry.TaskId));
        if (await SnoozePickerDialog.ShowAsync(owner, picker) is not DateTimeOffset until)
        {
            return false;
        }

        await _reminders.SnoozeAsync(entry, until);
        return true;
    }
}
