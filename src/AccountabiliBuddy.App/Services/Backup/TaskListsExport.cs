using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// The task lists in a backup (ADR 71), in their sync form (the phone reads and writes the same names). Since ADR 76 a backup also
/// holds the one task list itself (<paramref name="Tasks"/>, <paramref name="Labels"/>, <paramref name="Views"/>,
/// <paramref name="TaskLog"/>): a restore takes those in, and the old lists only when a backup has none (an older one). Since the
/// account's Tasks settings are shared it holds them too (<paramref name="AccountSettings"/>), taken in through
/// <see cref="Tasks.TaskAccountSettingsService"/>. Since ADR 87 the session notes shared between the user's own devices are in it too
/// (<paramref name="SessionNotes"/>, with their deletion marks): a restore merges them like another device's, so a note deleted
/// elsewhere since stays deleted. They are the user's private text, so only a backup the user holds carries them, never a log.
/// </summary>
public sealed record TaskListsExport(
    IReadOnlyList<SyncNextTask>? NextTasks,
    IReadOnlyList<SyncParked>? Comebacks,
    IReadOnlyList<SyncLaterItem>? Later,
    IReadOnlyList<SyncCategory>? Categories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskItem>? Tasks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskLabel>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskView>? Views = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskLogItem>? TaskLog = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncTaskAccountSettings? AccountSettings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncSessionNote>? SessionNotes = null);
