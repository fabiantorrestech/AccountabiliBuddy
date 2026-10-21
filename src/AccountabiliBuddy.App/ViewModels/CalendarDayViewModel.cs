namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One cell of the Exceptions month view. Rebuilt whenever the month, the selection or the day plan changes.</summary>
/// <param name="IsInMonth">False for the leading/trailing days of neighbouring months.</param>
/// <param name="IsAdjusted">The date has its own plan, different from its default (shown with a lighter background).</param>
/// <param name="Lines">At most <see cref="ExceptionsViewModel.MaxLinesPerDay"/> lines; the rest are counted in <paramref name="MoreText"/>.</param>
public sealed record CalendarDayViewModel(
    DateOnly Date,
    bool IsInMonth,
    bool IsToday,
    bool IsSelected,
    bool IsAdjusted,
    IReadOnlyList<CalendarDayLine> Lines,
    string? MoreText)
{
    public string DayNumber => Date.Day.ToString();

    public double Opacity => IsInMonth ? 1.0 : 0.45;
}
