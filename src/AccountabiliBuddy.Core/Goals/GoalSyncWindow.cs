namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which goal entries a device's regular sync file carries (ADR 79 size decision, owner 2026-11-14): those edited in the last
/// <see cref="Days"/> days by their stamp (edit time, or deletion time when later), by the trusted now passed in. Safe because a missing
/// entry never deletes anything on the receiver; older entries travel in the goal history files (<see cref="GoalHistoryRules"/>). The
/// other four goal parts travel whole. A newer app's entry whose stamp can't be read is carried (it can't be judged). The phone's twin
/// selects the same.
/// </summary>
public static class GoalSyncWindow
{
    public const int Days = 90;

    /// <summary>The window: <see cref="Days"/> days.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(Days);

    /// <summary>The entries whose stamp is at or after <paramref name="now"/> minus <see cref="Window"/>, in their order, newer items too.</summary>
    public static IReadOnlyList<GoalEntry> Recent(IReadOnlyList<GoalEntry> entries, DateTimeOffset now)
    {
        DateTimeOffset from = now - Window;

        // The list as written: newer items at their places among the known ones (GoalNewerJson.WriteList), each kept or not.
        IReadOnlyList<GoalNewerItem> newer = GoalNewerJson.NewerOf(entries).OrderBy(item => item.Index).ToList();
        var known = new List<GoalEntry>();
        var keptNewer = new List<GoalNewerItem>();
        int nextNewer = 0;
        int written = 0;
        void TakeNewer(bool all)
        {
            while (nextNewer < newer.Count && (all || newer[nextNewer].Index <= written))
            {
                GoalNewerItem item = newer[nextNewer++];
                if (GoalRawItem.TryRead(item.Json) is not { } raw || Later(raw.EditedAt, raw.DeletedAt) >= from)
                {
                    keptNewer.Add(item with { Index = known.Count + keptNewer.Count });
                }

                written++;
            }
        }

        foreach (GoalEntry entry in entries)
        {
            TakeNewer(all: false);
            if (entry is not null && entry.Stamp >= from)
            {
                known.Add(entry);
            }

            written++;
        }

        TakeNewer(all: true);
        return GoalNewerJson.WithNewer(known, keptNewer);
    }

    /// <summary><paramref name="lists"/> with only the recent entries (<see cref="Recent"/>); the other parts unchanged.</summary>
    public static GoalSyncLists Apply(GoalSyncLists lists, DateTimeOffset now)
    {
        return lists with { Entries = Recent(lists.Entries, now) };
    }

    private static DateTimeOffset Later(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return deletedAt is DateTimeOffset deleted && deleted > editedAt ? deleted : editedAt;
    }
}
