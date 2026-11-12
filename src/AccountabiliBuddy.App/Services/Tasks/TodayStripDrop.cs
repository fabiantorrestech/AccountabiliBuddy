using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The Tasks page's Today strip (round 2 R3, change note §2): a Shelf task dropped on it, or its row's ← (button or key), goes to
/// Today › Queued as an ordinary task-list move (<see cref="TaskActionsService.MoveAsync"/>: a toast with Undo, which puts it back in its
/// bin). Never into This session: the strip has no path there, so the session's action cap is never in play. Only live tasks in a Shelf
/// bin (To sort, Next, Later) move; anything else is left where it is.
/// </summary>
public static class TodayStripDrop
{
    /// <summary>The ids among <paramref name="ids"/> the strip takes: live tasks in a Shelf bin, each once.</summary>
    public static IReadOnlyList<Guid> Movable(TaskLists lists, IEnumerable<Guid> ids)
    {
        var shelf = lists.Tasks
            .Where(item => item.Task.IsLive && TaskBins.IsShelf(item.Task.Bin))
            .Select(item => item.Id)
            .ToHashSet();
        return ids.Distinct().Where(shelf.Contains).ToList();
    }

    /// <summary>Moves the Shelf tasks among <paramref name="ids"/> to Queued; returns how many moved (0 when none could).</summary>
    public static async Task<int> MoveToQueuedAsync(TaskActionsService actions, TaskStore store, IReadOnlyList<Guid> ids)
    {
        IReadOnlyList<Guid> movable = Movable(store.Lists, ids);
        if (movable.Count == 0)
        {
            return 0;
        }

        TaskMoveOutcome outcome = await actions.MoveAsync(movable, TaskBin.Queued);
        return outcome.Moved;
    }
}
