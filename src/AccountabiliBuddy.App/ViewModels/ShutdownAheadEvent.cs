using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One event card on the look-ahead step, in its calendar's colour.</summary>
/// <param name="Title">The event's title.</param>
/// <param name="TimeText">E.g. "9:00 – 9:30 AM", or "All day".</param>
/// <param name="Background">The calendar's colour (the theme's surface when the colour is not valid).</param>
/// <param name="Foreground">White or near-black, whichever reads better on <paramref name="Background"/>.</param>
/// <param name="IsAllDay">An all-day event shows its title alone, as in Google Calendar's agenda widget.</param>
public sealed record ShutdownAheadEvent(string Title, string TimeText, IBrush Background, IBrush Foreground, bool IsAllDay)
{
    public bool IsTimed => !IsAllDay;
}
