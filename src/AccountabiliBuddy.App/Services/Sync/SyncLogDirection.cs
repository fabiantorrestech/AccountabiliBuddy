namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Which way a logged sync pass went.</summary>
public enum SyncLogDirection
{
    /// <summary>This PC put its file on a method.</summary>
    Send,

    /// <summary>This PC read the other devices' files from a method.</summary>
    Read,

    /// <summary>A pass over the goal history files of a method.</summary>
    History,
}
