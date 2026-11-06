using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// The synced settings besides the profiles (ADR 71), read for a backup and restored through their own services, so a restore is an
/// ordinary edit: a looser part waits, and the change reaches the other devices. Each restore returns a note to show, or null.
/// </summary>
public sealed record SyncedSettingsAccess(
    Func<JsonElement> Sleep,
    Func<JsonElement, Task<string?>> RestoreSleep,
    Func<JsonElement> DayBar,
    Func<JsonElement, Task<string?>> RestoreDayBar,
    Func<JsonElement> Goal,
    Func<JsonElement, Task<string?>> RestoreGoal,
    Func<JsonElement> Mode,
    Func<JsonElement, Task<string?>> RestoreMode);
