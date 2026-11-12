namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>A calendar of the export account that the experimental "picked calendar" can use (Settings › Tasks › Reminders).</summary>
/// <param name="GoogleId">Google's id of the calendar (what <see cref="TaskCalendarExportService.SetPickedCalendarAsync"/> takes).</param>
/// <param name="Name">Its name as Google shows it.</param>
public sealed record TaskPickableCalendar(string GoogleId, string Name);
