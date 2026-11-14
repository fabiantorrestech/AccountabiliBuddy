using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A past task its owning PC deleted (here or by another device's settle record) in the last 30 days, as the owner keeps sending it in
/// <see cref="SyncTaskHistoryEntry.Deleted"/> (AR1b review, owner decision 2026-11-14) so every device can list it in Recently deleted and
/// Restore it (a newer settle record) even after the owner's file no longer carries it in <see cref="SyncTaskHistoryEntry.Tasks"/>.
/// Its text is the user's private text: only inside sealed sync files between the user's own devices, never logged.
/// </summary>
/// <param name="Task">The task as it was when deleted.</param>
/// <param name="DeletedAt">When the owner deleted it; the 30 days count from here, then the owner stops sending it.</param>
public sealed record SyncDeletedPastTask(SessionTask Task, DateTimeOffset DeletedAt)
{
    /// <summary>How long the owner keeps sending a deleted past task (the same window as the other deletion tombstones).</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(30);
}
