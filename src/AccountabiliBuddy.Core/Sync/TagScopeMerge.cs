namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two tag-scope lists (ADR 89) item by item with <see cref="SharedListMerge"/>: the newest version of each scope wins (a
/// tombstone newer than an edit makes the tag shared again), tombstones are kept 30 days, a time far ahead is clamped to 2 minutes
/// ahead. Either side null means none; nothing left is null, so a device that never used scopes keeps writing none. At most
/// <see cref="MaxScopes"/> entries are kept (a file's size): past it the oldest tombstones go first, then the oldest live scopes. The
/// phone's twin is <c>TagScopeMerge</c> in android/shared.
/// </summary>
public static class TagScopeMerge
{
    /// <summary>The most scopes (live and tombstones) a device keeps and sends.</summary>
    public const int MaxScopes = 2000;

    public static List<SyncTagScope>? Merge(IEnumerable<SyncTagScope>? held, IEnumerable<SyncTagScope>? incoming, DateTimeOffset now)
    {
        List<SyncTagScope> merged = SharedListMerge.Merge(
            (held ?? []).Where(scope => scope is not null),
            (incoming ?? []).Where(scope => scope is not null),
            now);
        return merged.Count == 0 ? null : Capped(merged);
    }

    // Past MaxScopes: the oldest tombstones go first, then the oldest live scopes; what stays keeps its (id) order.
    private static List<SyncTagScope> Capped(List<SyncTagScope> merged)
    {
        if (merged.Count <= MaxScopes)
        {
            return merged;
        }

        HashSet<SyncTagScope> drop = merged
            .OrderBy(scope => scope.DeletedAt is null ? 1 : 0)
            .ThenBy(scope => (scope.DeletedAt ?? scope.EditedAt).UtcTicks)
            .ThenBy(scope => scope.Id.ToString("D"), StringComparer.Ordinal)
            .Take(merged.Count - MaxScopes)
            .ToHashSet();
        return merged.Where(scope => !drop.Contains(scope)).ToList();
    }
}
