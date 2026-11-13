namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One line of the Archive's "Recently deleted" that every device can build from synced data (AR1-fix): a task deleted from the
/// one task list (its History entry) or a past task deleted on any device (its settle record, with a snapshot of its text).
/// </summary>
/// <param name="Id">The task's id.</param>
/// <param name="EntryId">The History entry that "Bring back" revives; null when the row has none (a past task: nothing here can revive it).</param>
public sealed record ArchiveDeletedRow(
    Guid Id,
    string Title,
    DateTimeOffset DeletedAt,
    ArchiveSource Source,
    string? DeviceName,
    Guid? EntryId)
{
    public bool CanRestore => EntryId is not null;
}
