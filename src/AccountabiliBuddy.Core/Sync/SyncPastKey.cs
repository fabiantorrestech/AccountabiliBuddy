namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A past group key (ADR 56): the group id and AES-256-GCM key a group had before a removal re-keyed it. Devices keep a few of these
/// and share them inside their sealed sync files, so a backup sealed with an earlier key can still be opened. It holds the derived AES
/// key, never the link code (the code also admits a device to the relay). Used only to open backups; never to read or write sync files.
/// </summary>
/// <param name="GroupId">The group id of that key: 8 lower-case hex characters, as in the header of a sealed file.</param>
/// <param name="Key">The 32-byte AES key as standard base64.</param>
/// <param name="RetiredAt">When the group left this key (the switch on the device that kept it). Keys older than a year are dropped.</param>
public sealed record SyncPastKey(string GroupId, string Key, DateTimeOffset RetiredAt)
{
    /// <summary>The most past keys a device keeps (the newest by <see cref="RetiredAt"/>).</summary>
    public const int MaxKept = 20;

    /// <summary>How long a past key is kept after <see cref="RetiredAt"/>.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(365);
}
