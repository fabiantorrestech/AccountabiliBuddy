namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// When this device first saw a classified calendar event at its current start, end and category (ADR 58). Changing any of
/// the three, or what the category does (its behaviour), starts the notice over. The record stays on this device; the sealed sync file carries its time as the event's "knownSince" (hard-rules, ADR 58).
/// </summary>
/// <param name="Behavior">What the category asked of the guards when the event was first seen.</param>
/// <param name="KnownAt">Trusted-clock time of the first sighting.</param>
public sealed record FirstKnownEntry(
    string CalendarId,
    string EventId,
    DateTime LocalStart,
    DateTime LocalEnd,
    Guid CategoryId,
    CategoryBehavior Behavior,
    DateTimeOffset KnownAt);
