namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two tag-scope lists (ADR 89) item by item with <see cref="SharedListMerge"/>: the newest version of each scope wins (a
/// tombstone newer than an edit makes the tag shared again), tombstones are kept 30 days, a time far ahead is clamped to 2 minutes
/// ahead. Either side null means none; nothing left is null, so a device that never used scopes keeps writing none. The phone's twin is
/// <c>TagScopeMerge</c> in android/shared.
/// </summary>
public static class TagScopeMerge
{
    public static List<SyncTagScope>? Merge(IEnumerable<SyncTagScope>? held, IEnumerable<SyncTagScope>? incoming, DateTimeOffset now)
    {
        List<SyncTagScope> merged = SharedListMerge.Merge(
            (held ?? []).Where(scope => scope is not null),
            (incoming ?? []).Where(scope => scope is not null),
            now);
        return merged.Count == 0 ? null : merged;
    }
}
