namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day with calendar events on the routine's look-ahead step: its date label on the left, its event cards stacked on the right.</summary>
/// <param name="Weekday">E.g. "Tue".</param>
/// <param name="DayNumber">E.g. "3".</param>
/// <param name="IsTomorrow">Tomorrow's day also shows "Tomorrow" under its day number.</param>
/// <param name="Events">The day's events (at most the per-day cap), in order.</param>
/// <param name="MoreText">E.g. "+2 more" when the day has more events than are shown; otherwise empty.</param>
public sealed record ShutdownAheadDay(string Weekday, string DayNumber, bool IsTomorrow, IReadOnlyList<ShutdownAheadEvent> Events, string MoreText)
{
    public bool HasMore => MoreText.Length > 0;
}
