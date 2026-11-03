namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A day plan category chosen on one calendar event or on its whole series (ADR 58). Kept on this device only, in the
/// documents table; it never goes into the shared profile JSON.
/// </summary>
/// <param name="CalendarId">The calendar the event is in.</param>
/// <param name="Key">
/// For <see cref="ClassificationScope.ThisEvent"/> the event's <see cref="CalendarEvent.ClassificationKey"/>; for
/// <see cref="ClassificationScope.WholeSeries"/> its <see cref="CalendarEvent.SeriesId"/>.
/// </param>
/// <param name="CategoryId">The category chosen; null = "Don't classify", which overrides a calendar rule.</param>
public sealed record EventClassification(string CalendarId, string Key, ClassificationScope Scope, Guid? CategoryId);
