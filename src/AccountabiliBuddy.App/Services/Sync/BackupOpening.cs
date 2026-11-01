using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>What opening a device backup gave (<see cref="BackupRestoreService.Open"/>, ADR 55).</summary>
/// <param name="File">The backup's sync file; set only when <paramref name="Kind"/> is <see cref="BackupOpeningKind.Ready"/>.</param>
/// <param name="Message">In plain words what to do when it can't be restored; null when it can.</param>
public sealed record BackupOpening(BackupOpeningKind Kind, SyncFile? File, string? Message)
{
    public bool IsReady => Kind == BackupOpeningKind.Ready;
}
