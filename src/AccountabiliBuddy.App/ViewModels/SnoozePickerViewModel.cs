using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Snooze until…" (spec 03 §C, 05 §8): "Ring again" over a big summary whose month, date, time (and year, when it isn't this year)
/// each lead to their field; the three shortcut chips with a pencil that edits them (<see cref="TaskReminderSettingsService"/>); a month
/// grid from Monday (no past months or days); Month / Date / Time / Year fields (a month already past this year reads "Jan (2027)" and
/// rolls the year, which flashes); natural typing ("thu 10:30"); and the status line with its checks. Snooze is off until the moment is
/// valid and ahead. The rules are <see cref="TaskSnoozePicker"/>'s and <see cref="TaskSnooze"/>'s; times are 12-hour.
/// </summary>
public sealed partial class SnoozePickerViewModel : ObservableObject
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly TaskReminderSettingsService _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _zone;
    private bool _syncing;

    public SnoozePickerViewModel(TaskReminderSettingsService settings, Func<DateTimeOffset> now, TimeZoneInfo zone, string? taskTitle)
    {
        _settings = settings;
        _now = now;
        _zone = zone;
        TaskTitle = taskTitle ?? string.Empty;

        // It opens at Tomorrow 9:00 AM; the fields follow in Refresh.
        _syncing = true;
        DateOnly today = Today;
        ShownMonth = new DateOnly(today.Year, today.Month, 1);
        Date = today.AddDays(1);
        Time = new TimeOnly(9, 0);
        IsPm = false;
        DayText = YearText = NaturalText = NaturalProblem = string.Empty;
        TimeText = TaskSnoozePicker.FieldText(Time.Value);
        Months = [];
        _syncing = false;
        Shortcuts = BuildShortcuts();
        Refresh();
    }

    /// <summary>The dialog closes with the picked moment (null: cancelled).</summary>
    public event EventHandler<DateTimeOffset?>? CloseRequested;

    /// <summary>The year rolled over by itself (a month or date already past): the year field flashes.</summary>
    public event EventHandler? YearRolled;

    public string TaskTitle { get; }

    public bool HasTaskTitle => TaskTitle.Length > 0;

    // ---- What is picked ----

    [ObservableProperty]
    public partial DateOnly Date { get; set; }

    /// <summary>The typed time; null while the field doesn't hold one.</summary>
    [ObservableProperty]
    public partial TimeOnly? Time { get; set; }

    /// <summary>The time field's AM/PM toggle (also A / P typed in the field).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmPmText))]
    public partial bool IsPm { get; set; }

    public string AmPmText => IsPm ? "PM" : "AM";

    // ---- The summary: "Thursday, " [Nov] [12th] [, 2027] · [9:00 AM] ----

    [ObservableProperty]
    public partial string WeekdayText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthPart { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DayPart { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasYearPart))]
    public partial string YearPart { get; set; } = string.Empty;

    public bool HasYearPart => YearPart.Length > 0;

    [ObservableProperty]
    public partial string TimePart { get; set; } = string.Empty;

    // ---- The fields ----

    /// <summary>"Jan" … "Dec"; a month already past this year (in the shown year) reads "Jan (2027)".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Months { get; set; }

    [ObservableProperty]
    public partial int MonthIndex { get; set; }

    [ObservableProperty]
    public partial string DayText { get; set; }

    [ObservableProperty]
    public partial string YearText { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; }

    /// <summary>The time field holds no time: red border.</summary>
    [ObservableProperty]
    public partial bool IsTimeInvalid { get; set; }

    // ---- The month grid ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle))]
    [NotifyCanExecuteChangedFor(nameof(PreviousMonthCommand))]
    public partial DateOnly ShownMonth { get; set; }

    public string MonthTitle => ShownMonth.ToString("MMMM yyyy", Invariant);

    [ObservableProperty]
    public partial IReadOnlyList<SnoozeDayCellViewModel> Days { get; set; } = [];

    public IReadOnlyList<string> WeekdayHeads { get; } = ["M", "T", "W", "T", "F", "S", "S"];

    // ---- Shortcuts ----

    [ObservableProperty]
    public partial IReadOnlyList<SnoozeShortcutViewModel> Shortcuts { get; set; }

    [ObservableProperty]
    public partial bool IsEditingShortcuts { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SnoozeShortcutEditViewModel> ShortcutEdits { get; set; } = [];

    // ---- Natural typing ----

    [ObservableProperty]
    public partial string NaturalText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNaturalProblem))]
    public partial string NaturalProblem { get; set; }

    public bool HasNaturalProblem => NaturalProblem.Length > 0;

    // ---- The status line and Snooze ----

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>The status line is a problem (red): the moment has passed.</summary>
    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    /// <summary>The picked moment when it is valid and ahead; null otherwise.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SnoozeCommand))]
    public partial DateTimeOffset? Moment { get; set; }

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now(), _zone).DateTime);

    [RelayCommand(CanExecute = nameof(CanSnooze))]
    private void Snooze()
    {
        Refresh();
        if (Moment is DateTimeOffset moment)
        {
            CloseRequested?.Invoke(this, moment);
        }
    }

    private bool CanSnooze() => Moment is not null;

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, null);
    }

    [RelayCommand]
    private void ToggleAmPm()
    {
        SetPm(!IsPm);
    }

    /// <summary>A or P typed in the time field, or the toggle.</summary>
    public void SetPm(bool pm)
    {
        IsPm = pm;
        Time = TaskSnoozePicker.ParseTime(TimeText, IsPm);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousMonth()
    {
        ShownMonth = ShownMonth.AddMonths(-1);
        Days = BuildDays();
    }

    private bool CanGoBack()
    {
        DateOnly today = Today;
        return ShownMonth > new DateOnly(today.Year, today.Month, 1);
    }

    [RelayCommand]
    private void NextMonth()
    {
        ShownMonth = ShownMonth.AddMonths(1);
        Days = BuildDays();
    }

    /// <summary>The time field lost the keyboard: a valid time is written out ("9:30" with its AM/PM).</summary>
    public void CommitTime()
    {
        if (Time is TimeOnly time)
        {
            _syncing = true;
            TimeText = TaskSnoozePicker.FieldText(time);
            IsPm = time.Hour >= 12;
            _syncing = false;
        }

        Refresh();
    }

    /// <summary>The date field lost the keyboard: a number there is taken; an empty one shows the picked day again.</summary>
    public void CommitDay()
    {
        if (int.TryParse(DayText, NumberStyles.None, Invariant, out int day) && day >= 1)
        {
            PickDate(TaskSnoozePicker.Upcoming(Today, Date.Month, day, Date.Year));
        }
        else
        {
            Refresh();
        }
    }

    /// <summary>The year field lost the keyboard: an unfinished year shows the picked one again.</summary>
    public void CommitYear()
    {
        Refresh();
    }

    [RelayCommand]
    private void ApplyNatural()
    {
        if (TaskSnoozePicker.Natural(NaturalText, _now(), _zone) is not { } found)
        {
            NaturalProblem = "Try “thu 10:30” or “tomorrow 9am”";
            return;
        }

        NaturalProblem = string.Empty;
        SetTime(found.Time);
        PickDate(found.Date);
    }

    [RelayCommand]
    private void EditShortcuts()
    {
        ShortcutEdits = _settings.Current.Shortcuts.Select(shortcut => new SnoozeShortcutEditViewModel(shortcut)).ToList();
        IsEditingShortcuts = true;
    }

    [RelayCommand]
    private void CancelShortcuts()
    {
        IsEditingShortcuts = false;
    }

    [RelayCommand]
    private async Task SaveShortcutsAsync()
    {
        List<TaskSnoozeShortcut> edited = ShortcutEdits.Select(edit => edit.Shortcut).OfType<TaskSnoozeShortcut>().ToList();
        if (edited.Count != 3)
        {
            return;
        }

        await _settings.UpdateAsync(current => current with { SnoozeShortcuts = edited });
        IsEditingShortcuts = false;
        Shortcuts = BuildShortcuts();
    }

    partial void OnMonthIndexChanged(int value)
    {
        if (_syncing || value is < 0 or > 11)
        {
            return;
        }

        PickDate(TaskSnoozePicker.Upcoming(Today, value + 1, Date.Day, Date.Year), fromMonthField: true);
    }

    // The date field takes digits only and commits once it can't grow (two digits, or 4 to 9).
    partial void OnDayTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        string digits = new(value.Where(char.IsAsciiDigit).Take(2).ToArray());
        if (digits != value)
        {
            DayText = digits;
            return;
        }

        if (digits.Length == 2 || digits.Length == 1 && digits[0] > '3')
        {
            CommitDay();
        }
    }

    // The year field takes four digits, then the date moves to that year (on to the next one when that day has passed).
    partial void OnYearTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        string digits = new(value.Where(char.IsAsciiDigit).Take(4).ToArray());
        if (digits != value)
        {
            YearText = digits;
            return;
        }

        if (digits.Length == 4 && int.TryParse(digits, NumberStyles.None, Invariant, out int year) && year is >= 1 and <= 9998)
        {
            PickDate(TaskSnoozePicker.Upcoming(Today, Date.Month, Date.Day, year), typedYear: year);
        }
    }

    partial void OnTimeTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        // Digits and a colon only; an A or P typed there flips the AM/PM toggle instead.
        string cleaned = new(value.Where(character => char.IsAsciiDigit(character) || character == ':').Take(5).ToArray());
        if (cleaned != value)
        {
            char half = value.LastOrDefault(character => character is 'a' or 'A' or 'p' or 'P');
            if (half != default)
            {
                IsPm = half is 'p' or 'P';
            }

            TimeText = cleaned;
            return;
        }

        Time = TaskSnoozePicker.ParseTime(value, IsPm);
        if (Time is TimeOnly time && time.Hour >= 13)
        {
            // Read as the 24-hour clock ("1530"): the toggle follows.
            IsPm = true;
        }

        Refresh();
    }

    private void SetTime(TimeOnly time)
    {
        _syncing = true;
        Time = time;
        IsPm = time.Hour >= 12;
        TimeText = TaskSnoozePicker.FieldText(time);
        _syncing = false;
    }

    // Picks a day; the grid shows its month. The year field flashes when the year changed by itself (a month or date already past),
    // or when a typed year had to move on because that day has passed.
    private void PickDate(DateOnly date, bool fromMonthField = false, int? typedYear = null)
    {
        bool rolled = typedYear is int typed ? date.Year != typed : (date.Year != Date.Year && (fromMonthField || date.Month == Date.Month));
        Date = date;
        ShownMonth = new DateOnly(date.Year, date.Month, 1);
        Refresh();
        if (rolled)
        {
            YearRolled?.Invoke(this, EventArgs.Empty);
        }
    }

    // Everything shown from the picked day and time: the summary, the fields, the grid, the status line.
    private void Refresh()
    {
        DateOnly today = Today;
        DateTimeOffset now = _now();
        _syncing = true;
        WeekdayText = Date.ToString("dddd", Invariant) + ", ";
        MonthPart = Date.ToString("MMM", Invariant);
        DayPart = TaskSnooze.Ordinal(Date.Day);
        YearPart = Date.Year == today.Year ? string.Empty : ", " + Date.Year.ToString(Invariant);
        TimePart = Time is TimeOnly time ? TaskSnooze.Clock(time) : "--:--";
        List<string> months = Enumerable.Range(1, 12)
            .Select(month =>
            {
                string name = new DateOnly(2000, month, 1).ToString("MMM", Invariant);
                return Date.Year == today.Year && month < today.Month ? $"{name} ({today.Year + 1})" : name;
            })
            .ToList();
        if (!months.SequenceEqual(Months))
        {
            Months = months;
        }

        MonthIndex = Date.Month - 1;
        DayText = Date.Day.ToString(Invariant);
        YearText = Date.Year.ToString(Invariant);
        _syncing = false;

        IsTimeInvalid = Time is null;
        Days = BuildDays();
        PreviousMonthCommand.NotifyCanExecuteChanged();
        if (Time is not TimeOnly picked)
        {
            Moment = null;
            StatusText = TaskSnoozePicker.TypeHint;
            IsStatusError = false;
            return;
        }

        Moment = TaskSnooze.Absolute(Date, picked, now, _zone);
        IsStatusError = Moment is null;
        StatusText = Moment is DateTimeOffset moment ? TaskSnoozePicker.Until(moment, now, _zone) : TaskSnoozePicker.PassedText;
    }

    private List<SnoozeDayCellViewModel> BuildDays()
    {
        DateOnly today = Today;
        return TaskSnoozePicker.MonthDays(ShownMonth.Year, ShownMonth.Month)
            .Select(day => new SnoozeDayCellViewModel(
                day, day.Month == ShownMonth.Month, day < today, day == today, day == Date,
                new RelayCommand(() => PickDate(day), () => day >= Today)))
            .ToList();
    }

    private List<SnoozeShortcutViewModel> BuildShortcuts()
    {
        return _settings.Current.Shortcuts
            .Select(shortcut =>
            {
                bool ahead = TaskSnooze.Resolve(shortcut, _now(), _zone) is not null;
                return new SnoozeShortcutViewModel(TaskSnooze.Label(shortcut), ahead, new RelayCommand(() => PickShortcut(shortcut), () => ahead));
            })
            .ToList();
    }

    private void PickShortcut(TaskSnoozeShortcut shortcut)
    {
        if (TaskSnooze.Resolve(shortcut, _now(), _zone) is not DateTimeOffset at)
        {
            return;
        }

        DateTime local = TimeZoneInfo.ConvertTime(at, _zone).DateTime;
        SetTime(TimeOnly.FromDateTime(local));
        PickDate(DateOnly.FromDateTime(local));
    }
}
