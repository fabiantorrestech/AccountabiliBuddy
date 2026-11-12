namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Where a live goal goes when its area isn't live (a deleted area, a backup's goal whose area didn't come): to "Work", made again from
/// the given template (its fixed id, name, icon and colour) when it is missing or a tombstone. One rule on the PC (<c>GoalStore</c>)
/// and the phone (<c>GoalListRules.tidy</c>), with stamps that depend only on the lists, never on the clock, so two devices tidying the
/// same lists get the same result (ADR 79): a moved goal is stamped one tick after the later of its own edit and its old area's
/// deletion; a "Work" made again in place of its tombstone keeps that place and is stamped one tick after it, and never before a goal
/// moved into it; a missing one goes last (after every area, tombstones included) stamped as the latest goal moved into it.
/// </summary>
public static class GoalAreaTidy
{
    public static (IReadOnlyList<GoalArea> Areas, IReadOnlyList<Goal> Goals) Rehome(IReadOnlyList<GoalArea> areas, IReadOnlyList<Goal> goals, GoalArea work)
    {
        var liveAreas = areas.Where(area => area.IsLive).Select(area => area.Id).ToHashSet();
        if (!goals.Any(goal => goal.IsLive && !liveAreas.Contains(goal.AreaId)))
        {
            return (areas, goals);
        }

        var byId = new Dictionary<Guid, GoalArea>();
        foreach (GoalArea area in areas)
        {
            byId.TryAdd(area.Id, area);
        }

        DateTimeOffset? workStamp = null;
        var placed = new List<Goal>(goals.Count);
        foreach (Goal goal in goals)
        {
            if (!goal.IsLive || liveAreas.Contains(goal.AreaId))
            {
                placed.Add(goal);
                continue;
            }

            DateTimeOffset basis = byId.TryGetValue(goal.AreaId, out GoalArea? gone) ? Later(goal.EditedAt, StampOf(gone)) : goal.EditedAt;
            DateTimeOffset stamp = basis.AddTicks(1);
            workStamp = workStamp is DateTimeOffset latest ? Later(latest, stamp) : stamp;
            placed.Add(goal with { AreaId = work.Id, EditedAt = stamp });
        }

        var tidied = areas.ToList();
        int index = tidied.FindIndex(area => area.Id == work.Id);
        if (index < 0)
        {
            double position = tidied.Count == 0 ? 0 : tidied.Max(area => area.Position) + 1;
            tidied.Add(work with { Position = position, EditedAt = workStamp!.Value, DeletedAt = null });
        }
        else if (!tidied[index].IsLive)
        {
            GoalArea tombstone = tidied[index];
            tidied[index] = work with { Position = tombstone.Position, EditedAt = Later(StampOf(tombstone).AddTicks(1), workStamp!.Value), DeletedAt = null };
        }

        return (tidied, placed);
    }

    // An area's newest stamp: its deletion when later than its edit.
    private static DateTimeOffset StampOf(GoalArea area)
    {
        return area.DeletedAt is DateTimeOffset deleted ? Later(area.EditedAt, deleted) : area.EditedAt;
    }

    // The later of two instants; the first on a tie (so both platforms keep the same offset).
    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second)
    {
        return second > first ? second : first;
    }
}
