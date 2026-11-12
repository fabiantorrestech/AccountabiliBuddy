using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Which tasks already belong to a session, so the next session's list can't hold them again (a late edit from another device that
/// brought one back from its tombstone is deleted again, ADR 48): every task of the running checklist and of each ended session's record,
/// except the open tasks of a record that ended with "End and mark the rest unfinished" (<see cref="SessionChecklist.UnfinishedToQueued"/>):
/// those went back to Queued as the same tasks and stay there (the running checklist that ends with it too, while its record is
/// written). A later session that takes one again makes it used once more.
/// </summary>
public static class SessionConsumed
{
    /// <summary>The ids of the tasks used by <paramref name="history"/> and, while it runs, by <paramref name="running"/>.</summary>
    public static HashSet<Guid> Ids(IEnumerable<SessionChecklist> history, SessionChecklist? running)
    {
        var consumed = new HashSet<Guid>();
        foreach (SessionChecklist record in history)
        {
            AddUsed(consumed, record);
        }

        if (running is { SessionStart: not null })
        {
            // The same rule: a checklist that is ending with the outcome (its record is being written) sends its open tasks back.
            AddUsed(consumed, running);
        }

        return consumed;
    }

    private static void AddUsed(HashSet<Guid> consumed, SessionChecklist checklist)
    {
        foreach (SessionTask task in checklist.Tasks)
        {
            if (!(checklist.UnfinishedToQueued && !task.IsDone))
            {
                consumed.Add(task.Id);
            }
        }
    }

    /// <summary>
    /// The record an ended checklist leaves: as it is, except that with "End and mark the rest unfinished" its open tasks (back in Queued as
    /// the same tasks) are left out, so a task id is in one record at most, and are counted as taken off open (<see cref="SessionChecklist.DroppedOpen"/>,
    /// ADR 25: the record never reads "all done").
    /// </summary>
    public static SessionChecklist Recorded(SessionChecklist ended)
    {
        int returned = ReturnedToQueued(ended).Count;
        return returned == 0
            ? ended
            : ended with { Tasks = ended.Tasks.Where(task => task.IsDone).ToList(), DroppedOpen = ended.DroppedOpen + returned };
    }

    /// <summary>The open tasks an ended session sends back to Queued: none unless it ended with "End and mark the rest unfinished".</summary>
    public static IReadOnlyList<SessionTask> ReturnedToQueued(SessionChecklist ended)
    {
        return ended.UnfinishedToQueued ? ended.Tasks.Where(task => !task.IsDone).ToList() : [];
    }
}
