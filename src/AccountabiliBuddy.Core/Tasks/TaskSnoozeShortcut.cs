namespace AccountabiliBuddy.Core.Tasks;

/// <summary>One of the three editable snooze shortcuts (Settings › Tasks › Reminders, the picker's pencil): a day and a time of day.</summary>
/// <param name="Weekday">Only for <see cref="SnoozeDay.Weekday"/>.</param>
public sealed record TaskSnoozeShortcut(SnoozeDay Day, TimeOnly Time, DayOfWeek Weekday = DayOfWeek.Monday);
