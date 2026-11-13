namespace AccountabiliBuddy.Core.Sync;

/// <summary>Why a sealed sync file did not open (ADR 86), told apart for the troubleshooting board.</summary>
public enum SyncFileOpenFailure
{
    /// <summary>Not a sync file, cut off, changed, or sealed with another key of the same group id.</summary>
    Damaged,

    /// <summary>A sync file whose header names another group: a leftover from before a device removal.</summary>
    OtherGroup,

    /// <summary>Opens, but its JSON is over the size limit.</summary>
    TooBig,

    /// <summary>Written by a newer app (a format version above this app's).</summary>
    NewerFormat,
}
