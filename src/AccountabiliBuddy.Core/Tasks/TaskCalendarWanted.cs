namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The event a task should have now (ADR 78), and in which calendar.</summary>
/// <param name="CalendarKey"><see cref="TaskCalendarMapping.AppCalendarKey"/> for the app's own calendar, or the picked calendar's Google id.</param>
/// <param name="Signature">A hash of the event's content, to tell whether a written event needs updating (no text in it).</param>
public sealed record TaskCalendarWanted(string CalendarKey, TaskCalendarEvent Event, string Signature);
