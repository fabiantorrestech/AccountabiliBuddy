using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// The goals for a backup (<see cref="GoalsExport"/>), carried with the task lists part. <see cref="Export"/> gets whether the file has a
/// password (notes and reasons only then) and returns null while this PC's goals aren't ready; <see cref="Import"/> takes the content
/// in through the goals store's guards (restore: the backup's live items stamped now win; merge: the newest edit wins) and returns a
/// line for the restore summary.
/// </summary>
public sealed record GoalsAccess(Func<bool, JsonElement?> Export, Func<JsonElement, bool, Task<string>> Import);
