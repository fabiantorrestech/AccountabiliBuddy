namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// One file a sync method holds that this PC does not take in (SYNC1b): why, since when, and whose it is when that can be told. The
/// real <see cref="Name"/> is kept only so "Try again" and "Remove my stale file" can name the file to the method; it is never shown,
/// logged or exported (the UI shows <see cref="Key"/>'s short form and the device's ordinal).
/// </summary>
/// <param name="Method">The sync method (<see cref="Core.Sync.SyncReceiptParts.Methods"/>).</param>
/// <param name="Key">An opaque id of the file on that method (a hash of its name).</param>
/// <param name="Name">The file's name on the method (internal).</param>
/// <param name="Reason">One of <see cref="Core.Sync.SyncReceiptParts.Failures"/>, <see cref="OtherGroup"/> or <see cref="OwnCopy"/>.</param>
/// <param name="At">This PC's trusted time when it was last seen so.</param>
/// <param name="Device">The device the file's name belongs to (by <c>FileNameFor</c> over the known devices); null when unknown.</param>
/// <param name="OwnStale">The file is this PC's own: an old-group name or an extra copy of its current file (the only files it may remove).</param>
/// <param name="Ignored">Hidden by "Ignore" (a line only: nothing else changes).</param>
public sealed record SyncUnreadableFile(string Method, string Key, string Name, string Reason, DateTimeOffset At, Guid? Device, bool OwnStale, bool Ignored)
{
    /// <summary>A leftover of another group (before a device removal): information, not a problem.</summary>
    public const string OtherGroup = "otherGroup";

    /// <summary>An extra copy of this PC's own file (Drive duplicate, a conflict copy in the folder): stale, never a problem.</summary>
    public const string OwnCopy = "ownCopy";

    /// <summary>Whether this entry is a problem (not an old-group leftover and not this PC's own extra copy).</summary>
    public bool IsProblem => Reason != OtherGroup && Reason != OwnCopy;
}
