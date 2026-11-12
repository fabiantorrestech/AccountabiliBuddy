namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges a shared list item by item (ADR 48): the next session's tasks and the come-backs. Each item has a stable id.
/// For one id the newest version wins; a version's time is its edit time, or its deletion time when that is later, so a
/// tombstone newer than the last edit removes the item and an edit newer than the tombstone brings it back. Items that
/// only one list has are all kept, so two devices editing different items both survive. A time more than
/// <see cref="FutureLimit"/> past the receiver's now is taken as that far ahead. Tombstones are kept for
/// <see cref="TombstonesKept"/>. The phone's twin is <c>SharedListMerge</c> in android/shared; both give the same result.
/// </summary>
public static class SharedListMerge
{
    /// <summary>How long a tombstone is kept and sent, so a device that was away that long can still learn of a deletion.</summary>
    public static readonly TimeSpan TombstonesKept = TimeSpan.FromDays(30);

    /// <summary>
    /// How far past the receiver's own time an incoming edit or deletion time may be: a later one (a device whose clock runs
    /// fast) is taken as this far ahead, so it can't win over every later edit for good.
    /// </summary>
    public static readonly TimeSpan FutureLimit = TimeSpan.FromMinutes(2);

    /// <returns>
    /// The merged list, tombstones included, ordered by <see cref="ISharedItem.Position"/> and then id; the same whichever
    /// list is <paramref name="first"/>.
    /// </returns>
    /// <param name="self">
    /// This device's id (ADR 72): a stub of an item this device keeps as a local one never replaces its own full copy of the
    /// same or a newer time. Null where local items play no part.
    /// </param>
    /// <param name="keepTombstones">Tombstones are kept for good, not <see cref="TombstonesKept"/> (labels: ids and stamps only, ADR 76).</param>
    public static List<T> Merge<T>(IEnumerable<T> first, IEnumerable<T> second, DateTimeOffset now, Guid? self = null, bool keepTombstones = false)
        where T : ISharedItem
    {
        var winners = new Dictionary<Guid, T>();
        DateTimeOffset limit = now + FutureLimit;
        foreach (T item in first.Concat(second).Select(item => Clamped(item, limit)))
        {
            if (!winners.TryGetValue(item.Id, out T? held) || Wins(item, held, self))
            {
                winners[item.Id] = item;
            }
        }

        // A local item's stub (ADR 72) is kept past the tombstones' 30 days: while it lives, an old copy can't come back.
        DateTimeOffset keptFrom = now - TombstonesKept;
        return winners.Values
            .Where(item => keepTombstones || item.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom || item.LocalTo is not null)
            .OrderBy(item => item.Position)
            .ThenBy(item => IdText(item), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The edit time for a local change to <paramref name="held"/> (null for a new item): <paramref name="now"/>, or one tick
    /// past the item's own newest time when that is later, so a change made right after a faster clock's edit still wins.
    /// </summary>
    public static DateTimeOffset NextStamp(DateTimeOffset now, ISharedItem? held)
    {
        if (held is null)
        {
            return now;
        }

        DateTimeOffset after = Stamp(held).AddTicks(1);
        return after > now ? after : now;
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

    /// <summary>
    /// The items as this device writes them into its own file (ADR 72): every local item is its stub, so the text stays with the
    /// keeper. Items that sync are unchanged.
    /// </summary>
    public static List<T> ForFile<T>(IEnumerable<T> items)
        where T : ISharedItem
    {
        return items.Select(item => item.LocalTo is not null && item.DeletedAt is null ? (T)item.AsLocalStub() : item).ToList();
    }

    /// <summary>Whether <paramref name="item"/> is shown: neither a tombstone nor a stub.</summary>
    public static bool IsLive(ISharedItem item)
    {
        return item.DeletedAt is null;
    }

    // The keeper's rule (ADR 72): its full copy of a local item stays over the item's stub (which every device, the keeper too,
    // writes) of the same or an older time; otherwise the usual order decides.
    private static bool Wins<T>(T candidate, T held, Guid? self)
        where T : ISharedItem
    {
        if (self is Guid me)
        {
            bool candidateIsMyFull = candidate.DeletedAt is null && candidate.LocalTo == me;
            bool heldIsMyFull = held.DeletedAt is null && held.LocalTo == me;
            bool candidateIsMyStub = candidate.DeletedAt is not null && candidate.LocalTo == me;
            bool heldIsMyStub = held.DeletedAt is not null && held.LocalTo == me;
            if (heldIsMyFull && candidateIsMyStub && Stamp(candidate) <= held.EditedAt)
            {
                return false;
            }

            if (candidateIsMyFull && heldIsMyStub && Stamp(held) <= candidate.EditedAt)
            {
                return true;
            }
        }

        return Compare(candidate, held) > 0;
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

    private static T Clamped<T>(T item, DateTimeOffset limit)
        where T : ISharedItem
    {
        if (item.EditedAt <= limit && (item.DeletedAt is not DateTimeOffset deletedAt || deletedAt <= limit))
        {
            return item;
        }

        DateTimeOffset edited = item.EditedAt > limit ? limit : item.EditedAt;
        DateTimeOffset? deleted = item.DeletedAt is DateTimeOffset deletedTime && deletedTime > limit ? limit : item.DeletedAt;
        return (T)item.WithStamps(edited, deleted);
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
