using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services;

// ADR 76: once the task migration ran, the next session's list and the categories are views of the one task list. The draft
// (and the running checklist) work as before; what used to be saved to "session.nextTasks" and "session.categories" goes to the
// task list as edits, and the task list's other changes (another device, another list) come back here.
public sealed partial class SessionTasksService
{
    // The task list this service's shared parts are a view of (null before the migration), and the items as last read from it.
    private TaskStore? _taskStore;
    private Dictionary<Guid, string> _draftShown = [];

    /// <summary>The next session's list as stored (tombstones and local items included), for the task migration (ADR 76).</summary>
    public IReadOnlyList<SyncNextTask> StoredNextTasks()
    {
        return _nextTasks.ToList();
    }

    /// <summary>
    /// From now on the next session's list and the categories are views of <paramref name="tasks"/> (ADR 76). While no session runs
    /// the draft shows the task list's next-session tasks; a running checklist stays as it is.
    /// </summary>
    public void UseTaskStore(TaskStore tasks)
    {
        _taskStore = tasks;
        ReadFromTaskStore();
        if (Current.SessionStart is null)
        {
            Current = DraftFromShared(Current.CurrentId).Grouped(_categories);
        }

        tasks.Changed += OnTaskStoreChanged;
    }

    private void OnTaskStoreChanged(object? sender, TaskStoreChangedEventArgs e)
    {
        if (e.Source == this)
        {
            return;
        }

        string before = DraftSignature();
        ReadFromTaskStore();
        if (DraftSignature() == before)
        {
            return;
        }

        _ = TakeTaskStoreChangeAsync();
    }

    // As a merge of the next session's list did: a task already in a session can't be on it, the draft shows the rest (a running
    // checklist stays as it is: the change shows when it ends).
    private async Task TakeTaskStoreChangeAsync()
    {
        List<SyncNextTask> remaining = TombstoneConsumed(_nextTasks, _time.GetUtcNow(), out bool tombstoned);
        if (tombstoned)
        {
            _nextTasks = remaining;
            await SaveNextTasksAsync();
        }

        if (Current.SessionStart is null)
        {
            await ShowSharedDraftAsync();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadFromTaskStore()
    {
        TaskStore tasks = _taskStore!;
        _nextTasks = TaskFacadeLists.Draft(tasks.Lists, _time.GetUtcNow());
        _draftShown = _nextTasks.ToDictionary(task => task.Id, task => task.TieBreak);
        _categories = tasks.Categories;
    }

    private string DraftSignature()
    {
        return string.Join("\n", _draftShown.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            + "\n" + string.Join("\n", _categories.Select(category => $"{category.Id:N} {category.Name} {category.Color}"));
    }

    // The draft's items changed since the task list was last read (tombstones for the ones taken off), sent there as edits.
    private Task SendNextTasksToTaskStore()
    {
        List<SyncNextTask> changed = _nextTasks.Where(task => !_draftShown.TryGetValue(task.Id, out string? shown) || shown != task.TieBreak).ToList();
        Task saved = _taskStore!.ApplyLocal(this, OldTaskFields.Empty with { NextTasks = changed });
        ReadFromTaskStore();
        return saved;
    }

    // The category list as it now is, sent to the task list as label edits; read back with the ids it keeps here.
    private Task SendCategoriesToTaskStore()
    {
        bool draftUnsent = _nextTasks.Any(task => !_draftShown.TryGetValue(task.Id, out string? shown) || shown != task.TieBreak)
            || _draftShown.Count != _nextTasks.Count;
        Task saved = _taskStore!.ApplyCategories(this, _categories);
        if (draftUnsent)
        {
            // The draft's own changes go with its next save; only the categories are read back now.
            _categories = _taskStore.Categories;
        }
        else
        {
            ReadFromTaskStore();
        }

        return saved;
    }
}
