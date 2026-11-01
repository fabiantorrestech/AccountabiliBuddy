namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// A device removed from the group (ADR 54), kept in the local document "sync.removed". Its files are refused until a file
/// from it opens with the group's new key, which means it was linked again.
/// </summary>
/// <param name="Name">The name it had when it was removed, for "Pixel 7 (removed)".</param>
/// <param name="RemovedAt">When this PC learned of the removal (its trusted clock).</param>
/// <param name="DataChoice">
/// What to do with the data received from it: <see cref="KeepData"/> (shown as removed in History and Stats) or
/// <see cref="EraseData"/>; null before the user chose (ADR 55). Set by the remove dialog.
/// </param>
public sealed record RemovedDevice(Guid DeviceId, string Name, DateTimeOffset RemovedAt, string? DataChoice = null)
{
    public const string KeepData = "keep";

    public const string EraseData = "erase";
}
