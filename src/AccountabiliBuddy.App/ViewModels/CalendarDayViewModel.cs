namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One cell of the Exceptions month view. Rebuilt whenever the month, the selection or the schedule changes.</summary>
/// <param name="IsInMonth">False for the leading/trailing days of neighbouring months.</param>
/// <param name="Lines">At most <see cref="ExceptionsViewModel.MaxLinesPerDay"/> lines; the rest are counted in <paramref name="MoreText"/>.</param>
public sealed record CalendarDayViewModel(
    DateOnly Date,
    bool IsInMonth,
    bool IsToday,
    bool IsSelected,
    IReadOnlyList<CalendarDayLine> Lines,
    string? MoreText)
{
    public string DayNumber => Date.Day.ToString();

    public double Opacity => IsInMonth ? 1.0 : 0.45;
}
