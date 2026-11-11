using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The sync file's old task fields (ADR 48, 50, 62, 72), which apps older than ADR 76 read and write: kept for one release beside
/// <see cref="SyncFile.Tasks"/>. A new PC rebuilds them from the task list on every write (<see cref="OldTaskFieldsWriter"/>) and
/// maps another device's into the task list when that file has no <see cref="SyncFile.Tasks"/> (<see cref="OldTaskFieldsReader"/>).
/// </summary>
/// <param name="NextTasks">This session, Queued and today's Finished.</param>
/// <param name="Later">Later and Next (marked <see cref="SyncLaterItem.NextBin"/>), with History's recently finished ones ticked.</param>
/// <param name="Parked">To sort, with History's recently finished ones ticked.</param>
/// <param name="Comebacks">The open To sort tasks, for the oldest phones.</param>
/// <param name="Categories">The labels as categories: name and opaque colour.</param>
public sealed record OldTaskFields(
    IReadOnlyList<SyncNextTask> NextTasks,
    IReadOnlyList<SyncLaterItem> Later,
    IReadOnlyList<SyncParked> Parked,
    IReadOnlyList<SyncComeback> Comebacks,
    IReadOnlyList<SyncCategory> Categories)
{
    public static OldTaskFields Empty { get; } = new([], [], [], [], []);

    /// <summary>The old fields a sync file carries (a missing part reads as empty).</summary>
    public static OldTaskFields Of(SyncFile file)
    {
        return new OldTaskFields(file.NextTasks ?? [], file.Later ?? [], file.Parked ?? [], file.Parked is null ? file.Comebacks ?? [] : [], file.Categories ?? []);
    }
}
