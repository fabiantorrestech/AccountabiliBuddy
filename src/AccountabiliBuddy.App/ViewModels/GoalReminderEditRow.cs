using System.Globalization;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One numbered row of Edit goal › Reminders (round 2 R5.4, board Goals-PC-Edit-Reminders): a day pill (Today / Every day / Sun / Fri 12/5),
/// a time pill (8:00 PM, always 12-hour), the repeat (Every day / Every week / Once), the reason ("if not done yet"; private text, shown on
/// screen only, never logged), ✎ to change it in place and × to remove it. <see cref="ToReminder"/> gives the stored reminder back.
/// </summary>
public sealed partial class GoalReminderEditRow : ObservableObject
{
    private static readonly string[] TimeFormats = ["h:mm tt", "h:mmtt", "h tt", "htt", "H:mm", "HH:mm", "h.mm tt"];

    private readonly GoalReminder _stored;
    private readonly DateOnly _today;
    private readonly Action<GoalReminderEditRow> _remove;
    private readonly Action _changed;
    private readonly IReadOnlyList<GoalWeekday>? _otherWeekdays;
    private bool _dayPicked;

    public GoalReminderEditRow(GoalReminder reminder, DateOnly today, bool startEditing, Action<GoalReminderEditRow> remove, Action changed)
    {
        _stored = reminder;
        _today = today;
        _remove = remove;
        _changed = changed;
        // A reminder on several weekdays (made on another screen) keeps them as one choice of its own.
        _otherWeekdays = reminder.Days == GoalReminderDays.Weekdays && reminder.Weekdays is { Count: > 1 } several ? several : null;
        DayChoices =
        [
            "Today", "Every day", "Every Sunday", "Every Monday", "Every Tuesday", "Every Wednesday", "Every Thursday", "Every Friday", "Every Saturday",
            "On a date", .. _otherWeekdays is null ? Array.Empty<string>() : [WeekdaysText(_otherWeekdays)],
        ];
        DayIndex = reminder.Days switch
        {
            GoalReminderDays.Today => 0,
            GoalReminderDays.EveryDay => 1,
            GoalReminderDays.Weekdays when _otherWeekdays is not null => DayChoices.Count - 1,
            GoalReminderDays.Weekdays => 2 + (int)(reminder.Weekdays?.FirstOrDefault() ?? GoalWeekday.Sunday),
            _ => 9,
        };
        Date = (reminder.Date ?? today).ToDateTime(TimeOnly.MinValue);
        TimeText = TimeShown(reminder.Time);
        Reason = reminder.Reason ?? string.Empty;
        IsEditing = startEditing;
        _dayPicked = false;
    }

    public Guid Id => _stored.Id;

    public IReadOnlyList<string> DayChoices { get; }

    [ObservableProperty]
    public partial int Number { get; set; }

    [ObservableProperty]
    public partial int DayIndex { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    /// <summary>"8:00 PM" (also "20:00" or "8 pm" when typed).</summary>
    [ObservableProperty]
    public partial string TimeText { get; set; }

    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public string NumberText => Number.ToString(CultureInfo.InvariantCulture);

    public bool ShowsDate => DayIndex == 9;

    /// <summary>The time as typed, or null when it can't be read.</summary>
    public TimeOnly? Time => ParseTime(TimeText);

    public string? Problem => Time is null ? "Type a time like 8:00 PM." : ShowsDate && Date is null ? "Pick the date." : null;

    public string DayPill => DayIndex switch
    {
        0 => "Today",
        1 => "Every day",
        9 => Date is DateTime date ? GoalTexts.Date(DateOnly.FromDateTime(date), _today) : "A date",
        _ when _otherWeekdays is not null && DayIndex == DayChoices.Count - 1 => WeekdaysText(_otherWeekdays),
        _ => ((DayOfWeek)(DayIndex - 2)).ToString()[..3],
    };

    public string TimePill => Time is TimeOnly time ? TimeShown(time) : TimeText;

    public string RepeatText => DayIndex switch
    {
        0 or 9 => "Once",
        1 => "Every day",
        _ => "Every week",
    };

    public bool HasReason => Reason.Length > 0;

    /// <summary>The reminder as set now, with the goal's rule switches.</summary>
    public GoalReminder ToReminder(double position, bool skipWhenDone, bool nudgeWhenBehind)
    {
        (GoalReminderDays Days, IReadOnlyList<GoalWeekday>? Weekdays, DateOnly? Date, bool Repeat) shape = DayIndex switch
        {
            // A stored "Today" reminder keeps the day it was set for unless its day was picked again here (milestone review S1:
            // saving the goal on a later day must never re-arm it for that day); a new row or a re-pick is for today.
            0 => (GoalReminderDays.Today, null, !_dayPicked && _stored.Days == GoalReminderDays.Today && _stored.Date is DateOnly kept ? kept : _today, false),
            1 => (GoalReminderDays.EveryDay, null, null, true),
            9 => (GoalReminderDays.Date, null, Date is DateTime picked ? DateOnly.FromDateTime(picked) : _today, false),
            _ when _otherWeekdays is not null && DayIndex == DayChoices.Count - 1 => (GoalReminderDays.Weekdays, _otherWeekdays, null, true),
            _ => (GoalReminderDays.Weekdays, new[] { (GoalWeekday)(DayIndex - 2) }, null, true),
        };
        (GoalReminderDays days, IReadOnlyList<GoalWeekday>? weekdays, DateOnly? date, bool repeat) = shape;
        string reason = Reason.Trim();
        return _stored with
        {
            Days = days,
            Weekdays = weekdays,
            Date = date,
            Repeat = repeat,
            Time = Time ?? _stored.Time,
            Reason = reason.Length == 0 ? null : reason,
            Position = position,
            SkipWhenDone = skipWhenDone,
            NudgeWhenBehind = nudgeWhenBehind,
        };
    }

    /// <summary>Reads "8:00 PM", "8:00pm", "8 PM", "20:00"; null for anything else.</summary>
    public static TimeOnly? ParseTime(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim().ToUpperInvariant();
        return TimeOnly.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out TimeOnly time) ? time : null;
    }

    [RelayCommand]
    private void Edit()
    {
        IsEditing = !IsEditing;
    }

    [RelayCommand]
    private void Remove()
    {
        _remove(this);
    }

    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(NumberText));

    partial void OnDayIndexChanged(int value)
    {
        _dayPicked = true;
        Edited();
    }

    partial void OnDateChanged(DateTime? value) => Edited();

    partial void OnTimeTextChanged(string value) => Edited();

    partial void OnReasonChanged(string value)
    {
        OnPropertyChanged(nameof(HasReason));
        _changed();
    }

    private void Edited()
    {
        foreach (string name in new[] { nameof(ShowsDate), nameof(Time), nameof(Problem), nameof(DayPill), nameof(TimePill), nameof(RepeatText) })
        {
            OnPropertyChanged(name);
        }

        _changed();
    }

    private static string TimeShown(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string WeekdaysText(IReadOnlyList<GoalWeekday> weekdays) => string.Join(", ", weekdays.Select(day => day.ToString()[..3]));
}
