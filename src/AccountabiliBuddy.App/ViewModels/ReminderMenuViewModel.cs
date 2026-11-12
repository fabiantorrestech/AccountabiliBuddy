using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Reminders &amp; due date menu of one task (spec 05 §7, 03 §C; 290 wide, "2 of 5"): its reminders with ×, the reminders it can
/// still add (At start time, 10 min before, 1 hour before, 1 day before at 9:00 AM; at most five), Can't miss (full screen), Ring it
/// now (test), Done by (Today 5:00 PM, Tomorrow, the next Friday and Monday, Pick a date…, No due date), and the task's own "Add to
/// Google Calendar" switch. Every change is a stamped task edit through <see cref="TaskActionsService"/> (one undo step each); a task's
/// first reminder or due date takes "Add new reminders to" (<see cref="TaskCalendarOptIn.WithDefault"/>, inside the action). The menu
/// stays open and shows each change at once. Times are 12-hour; dates "Fri 11/13".
/// </summary>
public sealed partial class ReminderMenuViewModel : ObservableObject
{
    // The relative rules the menu offers, in its order.
    private static readonly string[] Rules = [TaskReminder.AtStart, TaskReminder.TenMinutesBefore, TaskReminder.OneHourBefore, TaskReminder.DayBeforeAtNine];

    // "Today 5:00 PM" is offered until then.
    private static readonly TimeOnly EndOfDay = new(17, 0);

    private readonly Guid _taskId;
    private readonly Func<Guid, TaskItem?> _read;
    private readonly TaskActionsService _actions;
    private readonly Func<TaskCalendarDestination> _destination;
    private readonly Action<Guid>? _ringTest;
    private readonly TimeProvider _time;

    public ReminderMenuViewModel(
        Guid taskId, Func<Guid, TaskItem?> read, TaskActionsService actions, Func<TaskCalendarDestination> destination, Action<Guid>? ringTest,
        TimeProvider time)
    {
        _taskId = taskId;
        _read = read;
        _actions = actions;
        _destination = destination;
        _ringTest = ringTest;
        _time = time;
        CountText = RingNowSub = PickedTimeText = PickStatus = string.Empty;
        Refresh();
    }

    /// <summary>Ring it now was chosen: the popover closes (the reminder shows instead).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"2 of 5".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReminderMenuRow> Reminders { get; set; } = [];

    /// <summary>The reminders it can still add (none once it has five).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReminderMenuChoice> Presets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsFull { get; set; }

    [ObservableProperty]
    public partial bool IsCantMiss { get; set; }

    /// <summary>"Shows the full-screen alarm" or "Shows the reminder card".</summary>
    [ObservableProperty]
    public partial string RingNowSub { get; set; }

    public bool CanRingNow => _ringTest is not null;

    [ObservableProperty]
    public partial IReadOnlyList<ReminderMenuChoice> DueChoices { get; set; } = [];

    [ObservableProperty]
    public partial bool HasDue { get; set; }

    /// <summary>"Pick a date…" is open: a month calendar and a time box.</summary>
    [ObservableProperty]
    public partial bool IsPickingDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPick))]
    public partial DateTime? PickedDate { get; set; }

    /// <summary>The picker's time; blank for the whole day.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPick))]
    public partial string PickedTimeText { get; set; }

    /// <summary>"Done by Thu 11/12 · whole day", "Done by Thu 11/12 9:30 AM", or what is wrong.</summary>
    [ObservableProperty]
    public partial string PickStatus { get; set; }

    [ObservableProperty]
    public partial bool IsPickBad { get; set; }

    public bool CanApplyPick => PickedDate is not null && ParseTime(PickedTimeText, out _);

    /// <summary>The first day the picker offers: today.</summary>
    public DateTime FirstPickable => Today().ToDateTime(TimeOnly.MinValue);

    [ObservableProperty]
    public partial bool IsOnCalendar { get; set; }

    /// <summary>The task left the task list while the menu was open (finished, deleted elsewhere).</summary>
    [ObservableProperty]
    public partial bool IsGone { get; set; }

    /// <summary>Reads the task again and shows it as it is now.</summary>
    public void Refresh()
    {
        if (_read(_taskId) is not TaskItem task)
        {
            IsGone = true;
            Reminders = [];
            Presets = DueChoices = [];
            return;
        }

        IsGone = false;
        IReadOnlyList<TaskReminder> reminders = task.Reminders ?? [];
        CountText = $"{reminders.Count} of {TaskReminder.MaxPerTask}";
        IsFull = reminders.Count >= TaskReminder.MaxPerTask;
        Reminders = reminders.Select(reminder => RowOf(task, reminder)).ToList();
        Presets = IsFull
            ? []
            : Rules.Where(rule => reminders.All(reminder => reminder.Rule != rule))
                .Select(rule => new ReminderMenuChoice(RuleText(rule), MaterialIconKind.Plus, new AsyncRelayCommand(() => AddAsync(rule))))
                .ToList();
        IsCantMiss = task.CantMiss;
        RingNowSub = task.CantMiss ? "Shows the full-screen alarm" : "Shows the reminder card";
        DueChoices = DueChoicesFor(task);
        HasDue = task.DueOn is not null;
        IsOnCalendar = task.Calendar;
    }

    [RelayCommand]
    private Task ToggleCantMissAsync() => RunAsync(() => _actions.SetCantMissAsync([_taskId], !IsCantMiss));

    [RelayCommand]
    private Task ToggleCalendarAsync() => RunAsync(() => _actions.SetCalendarAsync([_taskId], !IsOnCalendar));

    [RelayCommand]
    private Task ClearDueAsync() => RunAsync(() => _actions.SetDueAsync([_taskId], null, null, _destination()));

    /// <summary>"Ring it now (test)": the reminder shows at once, as it would ring; nothing on the task changes.</summary>
    [RelayCommand]
    private void RingNow()
    {
        _ringTest?.Invoke(_taskId);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void TogglePickDate()
    {
        IsPickingDate = !IsPickingDate;
        if (IsPickingDate && PickedDate is null)
        {
            TaskItem? task = _read(_taskId);
            PickedDate = (task?.DueOn ?? Today().AddDays(1)).ToDateTime(TimeOnly.MinValue);
            PickedTimeText = task?.DueAt is TimeOnly at ? TaskSnooze.Clock(at) : string.Empty;
        }

        UpdatePickStatus();
    }

    [RelayCommand]
    private async Task ApplyPickAsync()
    {
        if (PickedDate is not DateTime date || !ParseTime(PickedTimeText, out TimeOnly? time))
        {
            UpdatePickStatus();
            return;
        }

        IsPickingDate = false;
        await RunAsync(() => _actions.SetDueAsync([_taskId], DateOnly.FromDateTime(date), time, _destination()));
    }

    partial void OnPickedDateChanged(DateTime? value) => UpdatePickStatus();

    partial void OnPickedTimeTextChanged(string value) => UpdatePickStatus();

    private void UpdatePickStatus()
    {
        if (PickedDate is not DateTime date)
        {
            PickStatus = "Pick a day";
            IsPickBad = false;
            return;
        }

        string day = DayText(DateOnly.FromDateTime(date));
        if (!ParseTime(PickedTimeText, out TimeOnly? time))
        {
            PickStatus = "Type a time like 5:30 PM, or leave it empty for the whole day";
            IsPickBad = true;
            return;
        }

        IsPickBad = false;
        PickStatus = time is TimeOnly at ? $"Done by {day} {TaskSnooze.Clock(at)}" : $"Done by {day} · whole day";
    }

    private Task AddAsync(string rule) => RunAsync(() => _actions.AddReminderAsync(_taskId, rule, null, _destination()));

    private Task RemoveAsync(Guid reminderId) => RunAsync(() => _actions.RemoveReminderAsync(_taskId, reminderId));

    private async Task RunAsync(Func<Task<bool>> edit)
    {
        await edit();
        Refresh();
    }

    // "Today 5:00 PM" (until 5 PM), Tomorrow, the next Friday and the next Monday (each once), the one the task has ticked.
    private List<ReminderMenuChoice> DueChoicesFor(TaskItem task)
    {
        DateOnly today = Today();
        var days = new List<(DateOnly Day, TimeOnly? At, string Text)>();
        if (TimeOnly.FromDateTime(LocalNow()) < EndOfDay)
        {
            days.Add((today, EndOfDay, "Today " + TaskSnooze.Clock(EndOfDay)));
        }

        days.Add((today.AddDays(1), null, "Tomorrow"));
        foreach (DayOfWeek weekday in new[] { DayOfWeek.Friday, DayOfWeek.Monday })
        {
            DateOnly next = NextWeekday(today, weekday);
            if (days.All(existing => existing.Day != next))
            {
                days.Add((next, null, DayText(next)));
            }
        }

        return days.Select(choice => new ReminderMenuChoice(
                choice.Text, MaterialIconKind.CalendarBlankOutline,
                new AsyncRelayCommand(() => RunAsync(() => _actions.SetDueAsync([_taskId], choice.Day, choice.At, _destination()))),
                IsChecked: task.DueOn == choice.Day && task.DueAt == choice.At))
            .ToList();
    }

    private ReminderMenuRow RowOf(TaskItem task, TaskReminder reminder)
    {
        string text = reminder.Rule == TaskReminder.Absolute && reminder.At is DateTimeOffset at
            ? MomentText(at)
            : RuleText(reminder.Rule);
        string sub;
        if (TaskReminderTimes.Occurrence(task, reminder, _time.LocalTimeZone) is DateTimeOffset rings)
        {
            sub = "Rings " + MomentText(rings);
        }
        else
        {
            sub = reminder.Rule is TaskReminder.AtStart or TaskReminder.TenMinutesBefore or TaskReminder.OneHourBefore or TaskReminder.DayBeforeAtNine
                ? "Needs a Done by date to ring"
                : string.Empty;
        }

        Guid id = reminder.Id;
        return new ReminderMenuRow(text, sub, new AsyncRelayCommand(() => RemoveAsync(id)));
    }

    private static string RuleText(string rule)
    {
        return rule switch
        {
            TaskReminder.AtStart => "At start time",
            TaskReminder.TenMinutesBefore => "10 min before",
            TaskReminder.OneHourBefore => "1 hour before",
            TaskReminder.DayBeforeAtNine => "1 day before at " + TaskSnooze.Clock(TaskReminderTimes.NineAm),
            _ => "From a newer version of the app",
        };
    }

    // "Fri 11/13 4:50 PM" in this PC's time zone.
    private string MomentText(DateTimeOffset moment)
    {
        DateTime local = TimeZoneInfo.ConvertTime(moment, _time.LocalTimeZone).DateTime;
        return $"{DayText(DateOnly.FromDateTime(local))} {TaskSnooze.Clock(TimeOnly.FromDateTime(local))}";
    }

    private static string DayText(DateOnly day) => day.ToString("ddd M/d", CultureInfo.InvariantCulture);

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;

    private DateOnly Today() => DateOnly.FromDateTime(LocalNow());

    // The next such weekday after today (a week on when today is that day).
    private static DateOnly NextWeekday(DateOnly from, DayOfWeek weekday)
    {
        int ahead = ((int)weekday - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(ahead == 0 ? 7 : ahead);
    }

    // What the time box takes: blank = the whole day (true, null); "5:30 PM", "5:30pm", "5 PM", "5p", "930" (9:30), "17:30". Without
    // AM or PM the hour is read on the 24-hour clock ("5" is 5:00 AM; the status line shows the result in 12-hour time before Set).
    // False when it isn't a time.
    private static bool ParseTime(string text, out TimeOnly? time)
    {
        time = null;
        string typed = text.Trim().ToUpperInvariant().Replace(".", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (typed.Length == 0)
        {
            return true;
        }

        bool? afternoon = null;
        foreach ((string suffix, bool pm) in new[] { ("AM", false), ("PM", true), ("A", false), ("P", true) })
        {
            if (typed.EndsWith(suffix, StringComparison.Ordinal))
            {
                afternoon = pm;
                typed = typed[..^suffix.Length];
                break;
            }
        }

        string hourText;
        string minuteText;
        int colon = typed.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            hourText = typed[..colon];
            minuteText = typed[(colon + 1)..];
        }
        else if (typed.Length <= 2)
        {
            hourText = typed;
            minuteText = "0";
        }
        else
        {
            // "930" or "1730": the last two digits are the minutes.
            hourText = typed[..^2];
            minuteText = typed[^2..];
        }

        if (!int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
            || !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out int minute) || minute > 59)
        {
            return false;
        }

        if (afternoon is bool pmTime)
        {
            if (hour is < 1 or > 12)
            {
                return false;
            }

            hour = hour % 12 + (pmTime ? 12 : 0);
        }
        else if (hour > 23)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
