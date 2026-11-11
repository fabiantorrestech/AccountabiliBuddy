namespace AccountabiliBuddy.App.Services;

/// <summary>One calendar of a Google account (calendarList).</summary>
/// <param name="Color">Google's background colour, "#rrggbb"; null if it has none.</param>
/// <param name="Description">The calendar's description; ADR 78 knows the app's own "AB Reminders" by it.</param>
public sealed record GoogleCalendarEntry(string Id, string Summary, string? Color, bool IsPrimary, string? Description = null);
