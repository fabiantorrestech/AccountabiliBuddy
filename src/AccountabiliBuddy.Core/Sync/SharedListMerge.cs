namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges a shared list item by item (ADR 48): the next session's tasks and the come-backs. Each item has a stable id.
/// For one id the newest version wins; a version's time is its edit time, or its deletion time when that is later, so a
/// tombstone newer than the last edit removes the item and an edit newer than the tombstone brings it back. Items that
/// only one list has are all kept, so two devices editing different items both survive. Tombstones are kept for
/// <see cref="TombstonesKept"/>. The phone's twin is <c>SharedListMerge</c> in android/shared; both give the same result.
/// </summary>
public static class SharedListMerge
{
    /// <summary>How long a tombstone is kept and sent, so a device that was away that long can still learn of a deletion.</summary>
    public static readonly TimeSpan TombstonesKept = TimeSpan.FromDays(30);

    /// <returns>
    /// The merged list, tombstones included, ordered by <see cref="ISharedItem.Position"/> and then id; the same whichever
    /// list is <paramref name="first"/>.
    /// </returns>
    public static List<T> Merge<T>(IEnumerable<T> first, IEnumerable<T> second, DateTimeOffset now)
        where T : ISharedItem
    {
        var winners = new Dictionary<Guid, T>();
        foreach (T item in first.Concat(second))
        {
            if (!winners.TryGetValue(item.Id, out T? held) || Compare(item, held) > 0)
            {
                winners[item.Id] = item;
            }
        }

        DateTimeOffset keptFrom = now - TombstonesKept;
        return winners.Values
            .Where(item => item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom)
            .OrderBy(item => item.Position)
            .ThenBy(item => IdText(item), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A position between two neighbours (either may be null at an end of the list).</summary>
    public static double PositionBetween(double? before, double? after)
    {
        return (before, after) switch
        {
            (double low, double high) => (low + high) / 2,
            (double low, null) => low + 1,
            (null, double high) => high - 1,
            _ => 1,
        };
    }

    /// <returns>More than 0 when <paramref name="candidate"/> is the version to keep over <paramref name="held"/>.</returns>
    private static int Compare<T>(T candidate, T held)
        where T : ISharedItem
    {
        int byTime = Stamp(candidate).UtcTicks.CompareTo(Stamp(held).UtcTicks);
        if (byTime != 0)
        {
            return byTime;
        }

        // The same time: a deletion wins, then the later position, then the fields, so the order the lists arrive in never matters.
        int byDeletion = (candidate.DeletedAt is not null).CompareTo(held.DeletedAt is not null);
        if (byDeletion != 0)
        {
            return byDeletion;
        }

        int byPosition = candidate.Position.CompareTo(held.Position);
        return byPosition != 0 ? byPosition : string.CompareOrdinal(candidate.TieBreak, held.TieBreak);
    }

    private static DateTimeOffset Stamp(ISharedItem item)
    {
        return item.DeletedAt is DateTimeOffset deletedAt && deletedAt > item.EditedAt ? deletedAt : item.EditedAt;
    }

    private static string IdText(ISharedItem item)
    {
        return item.Id.ToString("D");
    }
}
