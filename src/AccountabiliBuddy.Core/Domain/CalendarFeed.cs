namespace AccountabiliBuddy.Core.Domain;

/// <summary>What one iCal (.ics) download contained.</summary>
/// <param name="Name">The calendar's own name (X-WR-CALNAME), if it has one.</param>
/// <param name="Events">Occurrences within the requested range, repeating events expanded.</param>
public sealed record CalendarFeed(string? Name, IReadOnlyList<CalendarEvent> Events);
