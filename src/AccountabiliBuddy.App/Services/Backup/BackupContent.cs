using System.Text.Json;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// What a backup file holds (inside <see cref="BackupEnvelope"/>). A null part wasn't exported. Secrets (calendar
/// addresses, Google tokens and client secret, encrypted history fields) are in plain text here, so only a
/// password-protected file may contain them (<see cref="IncludesSecrets"/>).
/// </summary>
/// <param name="Profiles">The profile document as written by ProfileDocumentSerializer (migrated on restore).</param>
/// <param name="Settings">App documents by key, without encrypted fields.</param>
/// <param name="GoogleClientSecret">The Google OAuth client secret from calendar settings, with secrets only.</param>
public sealed record BackupContent(
    DateTimeOffset CreatedAt,
    string AppVersion,
    bool IncludesSecrets,
    string? Profiles,
    IReadOnlyDictionary<string, JsonElement>? Settings,
    string? GoogleClientSecret,
    IReadOnlyList<BackupCalendarSource>? CalendarSources,
    IReadOnlyList<CalendarEvent>? CalendarEvents,
    ActivityHistoryExport? History)
{
    public BackupParts Parts =>
        (Profiles is not null ? BackupParts.Profiles : BackupParts.None)
        | (Settings is not null ? BackupParts.Settings : BackupParts.None)
        | (CalendarSources is not null ? BackupParts.Calendars : BackupParts.None)
        | (History is not null ? BackupParts.History : BackupParts.None);
}
