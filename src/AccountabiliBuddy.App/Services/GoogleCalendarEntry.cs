namespace AccountabiliBuddy.App.Services;

/// <summary>One calendar of a Google account (calendarList).</summary>
/// <param name="Color">Google's background colour, "#rrggbb"; null if it has none.</param>
public sealed record GoogleCalendarEntry(string Id, string Summary, string? Color, bool IsPrimary);
