namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A day plan category chosen on one calendar event or on its whole series (ADR 58). Stored on this device only (the choice itself), in the
/// documents table; it never goes into the shared profile JSON. What it resolves to travels in the sealed sync file (hard-rules, ADR 58).
/// </summary>
/// <param name="CalendarId">The calendar the event is in.</param>
/// <param name="Key">
/// For <see cref="ClassificationScope.ThisEvent"/> the event's <see cref="CalendarEvent.ClassificationKey"/>; for
/// <see cref="ClassificationScope.WholeSeries"/> its <see cref="CalendarEvent.SeriesId"/>.
/// </param>
/// <param name="CategoryId">The category chosen; null = "Don't classify", which overrides a calendar rule.</param>
public sealed record EventClassification(string CalendarId, string Key, ClassificationScope Scope, Guid? CategoryId);
