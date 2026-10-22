namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A device linked to the one that wrote the file (ADR 22, Task 18): the phone's Pixel Watch. Shown under its device
/// in the Devices tab, display only; it's paired and unpaired on that device.
/// </summary>
/// <param name="Kind"><see cref="KindWatch"/>.</param>
/// <param name="Linked">Still known to the writing device (its watch link is still valid).</param>
/// <param name="LastHeard">When the writing device last heard from it or delivered to it; null when not yet.</param>
public sealed record SyncCompanion(string Name, string Kind, bool Linked, DateTimeOffset? LastHeard)
{
    public const string KindWatch = "watch";
}
