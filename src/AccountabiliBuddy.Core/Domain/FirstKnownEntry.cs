namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// When this device first saw a classified calendar event at its current start, end and category (ADR 58). Changing any of
/// the three starts the notice over. Kept on this device only.
/// </summary>
/// <param name="KnownAt">Trusted-clock time of the first sighting.</param>
public sealed record FirstKnownEntry(
    string CalendarId,
    string EventId,
    DateTime LocalStart,
    DateTime LocalEnd,
    Guid CategoryId,
    DateTimeOffset KnownAt);
