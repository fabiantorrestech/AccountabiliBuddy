using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The conflicts list's source over the task list (<see cref="TaskStore"/>) and the task actions; the dialog is the one Tasks opens.</summary>
public sealed class TaskConflictSource : ISyncConflictSource
{
    private readonly TaskStore _store;
    private readonly Func<TaskActionsService?> _actions;
    private readonly Func<Guid, (string Name, string Kind)?> _deviceOf;
    private readonly Func<Guid?> _self;
    private readonly TimeProvider _time;

    public TaskConflictSource(
        TaskStore store, Func<TaskActionsService?> actions, Func<Guid, (string Name, string Kind)?> deviceOf, Func<Guid?> self, TimeProvider time)
    {
        _store = store;
        _actions = actions;
        _deviceOf = deviceOf;
        _self = self;
        _time = time;
        store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SyncConflictItem> List()
    {
        return _store.Lists.Tasks
            .Where(item => item.Task.DeletedAt is null && item.Task.Conflicts is { Count: > 0 })
            .OrderByDescending(item => item.Task.EditedAt)
            .Select(item => new SyncConflictItem(item.Task.Id, item.Task.Title ?? string.Empty, item.Task.Conflicts!.Count))
            .ToList();
    }

    public ConflictDialogViewModel? DialogFor(Guid id)
    {
        if (_actions() is not { } actions || _store.Lists.Tasks.FirstOrDefault(item => item.Task.Id == id)?.Task.Conflicts is not { Count: > 0 })
        {
            return null;
        }

        return new ConflictDialogViewModel(_store, actions, id, _deviceOf, _self(), _time);
    }
}
