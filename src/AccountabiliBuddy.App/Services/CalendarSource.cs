namespace AccountabiliBuddy.App.Services;

/// <summary>
/// A place calendars come from: the in-app calendar, one iCal link, or one Google account.
/// Kept in the documents table (key "calendar.sources"); <see cref="ProtectedSecret"/> is DPAPI-encrypted.
/// </summary>
/// <param name="Name">What the user calls it, e.g. "Work account".</param>
/// <param name="ProtectedSecret">Base64 of the encrypted iCal address or Google refresh token; null for the in-app calendar.</param>
public sealed record CalendarSource(
    string Id,
    CalendarSourceKind Kind,
    string Name,
    IReadOnlyList<CalendarInfo> Calendars,
    string? ProtectedSecret = null);
