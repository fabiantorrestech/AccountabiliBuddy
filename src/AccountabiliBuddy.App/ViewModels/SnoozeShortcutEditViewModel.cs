using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One shortcut while the picker's pencil edits them: a day (Today, Tomorrow, Next Monday or a weekday) and a time ("6:00 PM",
/// "6pm", "930"). The result line shows how it reads, 12-hour.
/// </summary>
public sealed partial class SnoozeShortcutEditViewModel : ObservableObject
{
    /// <summary>The days a shortcut can name, in the order of the list.</summary>
    public static readonly IReadOnlyList<(SnoozeDay Day, DayOfWeek Weekday, string Text)> DayChoices =
    [
        (SnoozeDay.Today, DayOfWeek.Monday, "Today"),
        (SnoozeDay.Tomorrow, DayOfWeek.Monday, "Tomorrow"),
        (SnoozeDay.NextMonday, DayOfWeek.Monday, "Next Monday"),
        (SnoozeDay.Weekday, DayOfWeek.Monday, "Monday"),
        (SnoozeDay.Weekday, DayOfWeek.Tuesday, "Tuesday"),
        (SnoozeDay.Weekday, DayOfWeek.Wednesday, "Wednesday"),
        (SnoozeDay.Weekday, DayOfWeek.Thursday, "Thursday"),
        (SnoozeDay.Weekday, DayOfWeek.Friday, "Friday"),
        (SnoozeDay.Weekday, DayOfWeek.Saturday, "Saturday"),
        (SnoozeDay.Weekday, DayOfWeek.Sunday, "Sunday"),
    ];

    public SnoozeShortcutEditViewModel(TaskSnoozeShortcut shortcut)
    {
        int index = DayChoices.ToList().FindIndex(choice => choice.Day == shortcut.Day && (shortcut.Day != SnoozeDay.Weekday || choice.Weekday == shortcut.Weekday));
        DayIndex = Math.Max(0, index);
        TimeText = TaskSnooze.Clock(shortcut.Time);
    }

    public IReadOnlyList<string> Days { get; } = DayChoices.Select(choice => choice.Text).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(IsValid))]
    public partial int DayIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(IsValid))]
    public partial string TimeText { get; set; }

    public bool IsValid => Shortcut is not null;

    /// <summary>"Tonight 6:00 PM", or what to type.</summary>
    public string Result => Shortcut is TaskSnoozeShortcut shortcut ? TaskSnooze.Label(shortcut) : "Type a time like 6pm or 9:30 AM";

    /// <summary>The edited shortcut; null while the time isn't one.</summary>
    public TaskSnoozeShortcut? Shortcut
    {
        get
        {
            if (TaskSnoozePicker.ParseClock(TimeText) is not TimeOnly time || DayIndex < 0 || DayIndex >= DayChoices.Count)
            {
                return null;
            }

            (SnoozeDay day, DayOfWeek weekday, _) = DayChoices[DayIndex];
            return new TaskSnoozeShortcut(day, time, weekday);
        }
    }
}
