namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One line of the Archive's "Recently deleted" that every device can build from synced data (AR1-fix, AR1b): a task deleted from the
/// one task list (its History entry) or a past task deleted on any device (its settle record, with a snapshot of its text taken from
/// any copy this device holds). The same on every device that holds the same data.
/// </summary>
/// <param name="Id">The task's id.</param>
/// <param name="EntryId">The History entry that "Bring back" revives; null when the row has none (a past task: Restore stamps a settle record).</param>
/// <param name="RestoreAs">A past task's settle state for Restore (<c>Done</c> when it was done when deleted, else <c>Open</c>); null for a History entry's row.</param>
public sealed record ArchiveDeletedRow(
    Guid Id,
    string Title,
    DateTimeOffset DeletedAt,
    ArchiveSource Source,
    string? DeviceName,
    Guid? EntryId,
    string? RestoreAs = null)
{
    /// <summary>Restore brings it back through its History entry (the task list).</summary>
    public bool CanRestore => EntryId is not null;

    /// <summary>Restore stamps a newer settle record over the delete, so the PC whose history held the task puts it back and every device lists it again (never a raw restore).</summary>
    public bool CanRestoreByStamp => EntryId is null && RestoreAs is not null;
}
