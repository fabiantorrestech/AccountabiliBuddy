namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// An event this PC wrote for a task (ADR 78), kept device-local: only events recorded here are ever changed or deleted.
/// </summary>
/// <param name="CalendarKey">As in <see cref="TaskCalendarWanted.CalendarKey"/>.</param>
/// <param name="CalendarId">The Google calendar it is in.</param>
/// <param name="EventId">Its Google event id (<see cref="TaskCalendarMapping.EventId"/>).</param>
/// <param name="Signature">The <see cref="TaskCalendarWanted.Signature"/> it was written with.</param>
public sealed record TaskCalendarRecord(string CalendarKey, string CalendarId, string EventId, string Signature);
