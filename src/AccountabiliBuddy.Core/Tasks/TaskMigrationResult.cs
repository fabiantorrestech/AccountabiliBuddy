using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What the task migration (ADR 76) made: the new lists in their sync form, the view searches that stay local, and the counts.</summary>
/// <param name="Tasks">Every task, tombstones included.</param>
/// <param name="Labels">The labels.</param>
/// <param name="Views">The built-in views (Default carrying the old current filter) and the old presets.</param>
/// <param name="History">History entries (finished).</param>
/// <param name="ViewSearches">An old preset's search words by view id: device-local, never synced.</param>
/// <param name="Counts">What went where.</param>
public sealed record TaskMigrationResult(
    IReadOnlyList<SyncTaskItem> Tasks,
    IReadOnlyList<SyncTaskLabel> Labels,
    IReadOnlyList<SyncTaskView> Views,
    IReadOnlyList<SyncTaskLogItem> History,
    IReadOnlyDictionary<Guid, string> ViewSearches,
    TaskMigrationCounts Counts);
