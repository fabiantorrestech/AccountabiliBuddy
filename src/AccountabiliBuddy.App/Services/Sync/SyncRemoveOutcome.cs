namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>What "Remove my stale file" came to on a folder or Drive (SYNC1b review fixes).</summary>
public enum SyncRemoveOutcome
{
    /// <summary>The file is not this PC's own stale copy (or could not be deleted): nothing changed.</summary>
    Refused,

    /// <summary>The file was deleted.</summary>
    Removed,

    /// <summary>The file was already gone (another device or the user removed it first): the list entry is cleared.</summary>
    AlreadyGone,
}
