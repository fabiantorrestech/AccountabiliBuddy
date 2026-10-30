using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// One deleted task, step, Later item or come-back, with everything it needs to come back as it was (ADR 50: private text,
/// kept only on this PC, DPAPI-encrypted by <see cref="RecentlyDeletedStore"/>, never logged and never synced; the tombstones
/// that sync carry no text).
/// </summary>
/// <param name="EntryId">The entry's own id (the item's id is inside it).</param>
/// <param name="Title">What the Undo bar and Recently deleted show.</param>
/// <param name="Task">The task (<see cref="DeletedKind.Task"/>, <see cref="DeletedKind.HistoryTask"/>), steps and fields included.</param>
/// <param name="Step">The step (<see cref="DeletedKind.Step"/>, <see cref="DeletedKind.HistoryStep"/>, <see cref="DeletedKind.LaterStep"/>).</param>
/// <param name="ParentId">A step's task (for a Later step, the Later item).</param>
/// <param name="Index">Its place among its siblings in the list it left.</param>
/// <param name="ChecklistStart">
/// The start of the session checklist it left (null: the draft, the next session's list, or the day's done-outside-a-session
/// record). Undo counts the "taken off open" back only when it comes back open into the same one.
/// </param>
/// <param name="WasLive">The checklist belonged to a running session when it was deleted.</param>
public sealed record DeletedItem(
    Guid EntryId,
    DateTimeOffset DeletedAt,
    DeletedKind Kind,
    string Title,
    SessionTask? Task = null,
    SessionSubtask? Step = null,
    Guid? ParentId = null,
    int Index = 0,
    DateTimeOffset? ChecklistStart = null,
    bool WasLive = false,
    SyncLaterItem? Later = null,
    Comeback? ComeBack = null)
{
    /// <summary>The item's own id.</summary>
    public Guid ItemId => Kind switch
    {
        DeletedKind.Task or DeletedKind.HistoryTask => Task!.Id,
        DeletedKind.Step or DeletedKind.HistoryStep or DeletedKind.LaterStep => Step!.Id,
        DeletedKind.Later => Later!.Id,
        _ => ComeBack!.Id,
    };

    public bool IsStep => Kind is DeletedKind.Step or DeletedKind.HistoryStep or DeletedKind.LaterStep;
}
