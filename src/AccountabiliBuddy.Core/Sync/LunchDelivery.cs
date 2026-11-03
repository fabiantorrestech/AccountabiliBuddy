namespace AccountabiliBuddy.Core.Sync;

/// <summary>Another active device's latest received file, as far as the lunch goes: its name, when it wrote the file, and the lunch in it (null: none).</summary>
public sealed record LunchPeer(string Name, DateTimeOffset WrittenAt, SyncLunch? Lunch);

/// <summary>
/// Whether the other devices have this device's lunch, from the latest file received from each (the phone's LunchDelivery is the same rule).
/// A device has it when its file holds the same start and end, or a newer edit. A device whose file is older than <see cref="AbsentAfter"/>
/// is off or not syncing, and is left out rather than reported on all day; the caller leaves out watches (they write no file) and removed devices.
/// </summary>
public static class LunchDelivery
{
    public static readonly TimeSpan AbsentAfter = TimeSpan.FromHours(12);

    /// <summary>The devices that do not have <paramref name="ours"/> yet, sorted by name; empty when all have it or there is no other device.</summary>
    public static IReadOnlyList<LunchPeer> Missing(SyncLunch ours, IEnumerable<LunchPeer> peers, DateTimeOffset now) =>
        peers.Where(peer => Present(peer, now) && !Has(peer.Lunch, ours))
            .OrderBy(peer => peer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The card's line: "On all your devices", "Not on Laptop yet · last heard 12:03 PM" (<paramref name="time"/> makes the 12-hour text), or null with no other device.</summary>
    public static string? Line(SyncLunch ours, IReadOnlyCollection<LunchPeer> peers, DateTimeOffset now, Func<DateTimeOffset, string> time)
    {
        if (!peers.Any(peer => Present(peer, now)))
        {
            return null;
        }

        IReadOnlyList<LunchPeer> missing = Missing(ours, peers, now);
        if (missing.Count == 0)
        {
            return "On all your devices";
        }

        LunchPeer first = missing[0];
        string names = missing.Count == 1 ? first.Name : $"{first.Name} and {missing.Count - 1} more";
        return $"Not on {names} yet · last heard {time(first.WrittenAt)}";
    }

    private static bool Present(LunchPeer peer, DateTimeOffset now) => now - peer.WrittenAt <= AbsentAfter;

    private static bool Has(SyncLunch? theirs, SyncLunch ours) =>
        theirs is not null && ((theirs.Start == ours.Start && theirs.End == ours.End) || theirs.LastEdit > ours.LastEdit);
}
