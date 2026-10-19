using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>Events of one Google calendar: everything in range (full fetch) or only what changed (incremental).</summary>
/// <param name="CancelledIds">Events deleted or cancelled since the last fetch (incremental only).</param>
public sealed record GoogleEventChanges(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<string> CancelledIds);
