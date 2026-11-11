using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// The task lists in a backup (ADR 71), in their sync form (the phone reads and writes the same names). Since ADR 76 a backup also
/// holds the one task list itself (<paramref name="Tasks"/>, <paramref name="Labels"/>, <paramref name="Views"/>,
/// <paramref name="TaskLog"/>): a restore takes those in, and the old lists only when a backup has none (an older one).
/// </summary>
public sealed record TaskListsExport(
    IReadOnlyList<SyncNextTask>? NextTasks,
    IReadOnlyList<SyncParked>? Comebacks,
    IReadOnlyList<SyncLaterItem>? Later,
    IReadOnlyList<SyncCategory>? Categories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskItem>? Tasks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskLabel>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskView>? Views = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskLogItem>? TaskLog = null);
