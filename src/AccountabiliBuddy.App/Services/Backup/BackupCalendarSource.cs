namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>A calendar source in a backup: the source without its encrypted secret, plus the secret itself (iCal
/// address or Google refresh token) when the backup includes secrets.</summary>
public sealed record BackupCalendarSource(CalendarSource Source, string? Secret);
