namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>How a device backup can be used on this PC (<see cref="BackupRestoreService.Open"/>).</summary>
public enum BackupOpeningKind
{
    /// <summary>It opens with this PC's group key.</summary>
    Ready,

    /// <summary>This PC isn't in a device group, so nothing can be opened yet.</summary>
    NotInGroup,

    /// <summary>It is a sync file of another group (another key).</summary>
    DifferentGroup,

    /// <summary>Not a backup this app can read (cut off, changed, or from a newer app).</summary>
    Unreadable,

    /// <summary>It is this PC's own data.</summary>
    ThisPc,
}
