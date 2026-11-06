using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>The task lists in a backup (ADR 71), in their sync form (the phone reads and writes the same names).</summary>
public sealed record TaskListsExport(
    IReadOnlyList<SyncNextTask>? NextTasks,
    IReadOnlyList<SyncParked>? Comebacks,
    IReadOnlyList<SyncLaterItem>? Later,
    IReadOnlyList<SyncCategory>? Categories);
