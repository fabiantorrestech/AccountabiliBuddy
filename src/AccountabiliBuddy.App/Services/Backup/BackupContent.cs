using System.Text.Json;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// What a backup file holds (inside <see cref="BackupEnvelope"/>). A null part wasn't exported. Secrets (calendar
/// addresses, Google tokens and client secret, encrypted history fields) are in plain text here, so only a
/// password-protected file may contain them (<see cref="IncludesSecrets"/>).
/// ADR 71: a backup is one <see cref="Kind"/> (<see cref="BackupKind"/>): synced settings (<see cref="Profiles"/> and <see cref="Synced"/>,
/// plus <see cref="Tasks"/> while task lists are shared), this device's settings (<see cref="Device"/> and the calendars, for the
/// <see cref="Platform"/> named, refused on the other), task lists, or the history. A file from before kinds has none and keeps its parts.
/// </summary>
/// <param name="Goals">Round 2 R4: this PC's goals (<see cref="GoalsExport"/>), carried with the task lists part; the phone ignores it.</param>
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
    ActivityHistoryExport? History,
    string? Kind = null,
    string? Platform = null,
    IReadOnlyDictionary<string, JsonElement>? Synced = null,
    IReadOnlyDictionary<string, JsonElement>? Device = null,
    JsonElement? Tasks = null,
    JsonElement? Goals = null)
{
    public BackupParts Parts =>
        (Profiles is not null ? BackupParts.Profiles : BackupParts.None)
        | (Settings is not null ? BackupParts.Settings : BackupParts.None)
        | (CalendarSources is not null ? BackupParts.Calendars : BackupParts.None)
        | (History is not null ? BackupParts.History : BackupParts.None)
        | (Synced is not null ? BackupParts.SyncedSettings : BackupParts.None)
        | (Device is not null ? BackupParts.DeviceSettings : BackupParts.None)
        | (Tasks is not null || Goals is not null ? BackupParts.TaskLists : BackupParts.None);
}
