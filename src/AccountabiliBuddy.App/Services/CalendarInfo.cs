namespace AccountabiliBuddy.App.Services;

/// <summary>One calendar of a <see cref="CalendarSource"/>, with how it is shown.</summary>
/// <param name="Color">"#RRGGBB".</param>
/// <param name="IsVisible">Hidden calendars are still fetched and kept; they are only left out of the month view.</param>
public sealed record CalendarInfo(string Id, string Name, string Color, bool IsVisible = true);
