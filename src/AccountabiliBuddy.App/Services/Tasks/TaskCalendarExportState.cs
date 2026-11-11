using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// What task export to Google Calendar keeps on this PC (ADR 78; document "tasks.calendarExport", device-local, never synced or
/// backed up): ids and hashes only, no titles.
/// </summary>
/// <param name="SourceId">The connected Google account (calendar source id) the events are in; null before the first export.</param>
/// <param name="AppCalendarId">The Google id of the app's own "AB Reminders" calendar; null until found or made.</param>
/// <param name="Records">The events this PC wrote, by task id: the only events it ever changes or deletes.</param>
public sealed record TaskCalendarExportState(
    string? SourceId,
    string? AppCalendarId,
    IReadOnlyDictionary<Guid, TaskCalendarRecord> Records)
{
    public static TaskCalendarExportState Empty { get; } = new(null, null, new Dictionary<Guid, TaskCalendarRecord>());
}
