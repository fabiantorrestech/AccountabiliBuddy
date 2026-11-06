using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// The task lists for a backup (ADR 71): tasks, Later, come-backs and task categories in their sync form, deletions included.
/// <see cref="Import"/> merges them by the usual rules (newest edit wins); with restore set, the backup's live items are stamped now
/// first, so they come back over later edits and deletions (items made since stay; nothing is deleted by an old backup).
/// </summary>
public sealed record TaskListsAccess(Func<JsonElement> Export, Func<JsonElement, bool, Task<string>> Import);
