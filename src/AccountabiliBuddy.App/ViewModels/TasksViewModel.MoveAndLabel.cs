using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 8c (SPEC "Move and label dialog", boards 21, 31): the Tasks page hosts Move and label. It opens from Batch edit (N)…,
// Ctrl+E with 2 or more selected, the select bar's Move or label (N) and a drop on the drag bar's choose zone (RequestMoveAndLabel), with
// the tasks brought along; the selection stays while it is open, and closing it clears the selection. Apply goes through the task actions
// (one undo step); an undo of it takes its ticks back (TaskActionsService.Undone).
public sealed partial class TasksViewModel : IMoveAndLabelHost
{
    /// <summary>Move and label while it is open; null otherwise.</summary>
    public MoveAndLabelViewModel? MoveAndLabel { get; private set; }

    public bool ShowsMoveAndLabel => MoveAndLabel is not null;

    /// <summary>Opens Move and label with <paramref name="ids"/> (the ones the task list holds); nothing before the task list exists.</summary>
    internal void OpenMoveAndLabel(IReadOnlyList<Guid> ids)
    {
        if (_actions is null || _store is not { IsActive: true })
        {
            return;
        }

        List<Guid> held = ids.Where(id => StoredTask(id) is not null).Distinct().ToList();
        if (held.Count == 0)
        {
            return;
        }

        if (IsDetailOpen)
        {
            CloseDetail();
        }

        MoveAndLabel?.Dispose();
        MoveAndLabel = new MoveAndLabelViewModel(this, held);
        RaiseMoveAndLabel();
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Done, × or Esc (8c.06): the dialog closes and the selection clears.</summary>
    public void CloseMoveAndLabel()
    {
        if (MoveAndLabel is null)
        {
            return;
        }

        MoveAndLabel.Dispose();
        MoveAndLabel = null;
        RaiseMoveAndLabel();
        ClearSelection();
    }

    private void RaiseMoveAndLabel()
    {
        OnPropertyChanged(nameof(MoveAndLabel));
        OnPropertyChanged(nameof(ShowsMoveAndLabel));
    }

    private void OnActionUndone(object? sender, TaskUndoEntry entry) => MoveAndLabel?.OnUndone(entry);

    // ---- IMoveAndLabelHost ----

    TaskItem? IMoveAndLabelHost.TaskOf(Guid id) => StoredTask(id);

    MoveAndLabelLabel? IMoveAndLabelHost.ShownLabelOf(TaskItem task) => LabelOf(task.LabelId) is { } label ? LookOf(label) : null;

    MoveAndLabelLabel? IMoveAndLabelHost.LabelById(Guid labelId) => LabelOf(labelId) is { } label ? LookOf(label) : null;

    IReadOnlyList<string> IMoveAndLabelHost.ShownTags(IReadOnlyList<string>? tags) => ShownTagsOf(tags);

    IReadOnlyList<string> IMoveAndLabelHost.KnownTags() => _store is { IsActive: true } store ? store.VisibleTags() : [];

    LabelPickerViewModel IMoveAndLabelHost.NewLabelPicker(IReadOnlyList<Guid?> currentLabels, LabelPick pick)
    {
        IReadOnlyList<TaskLabelGroup> groups = _store is { IsActive: true } store ? TaskLabelGroupBook.Live(store.Lists.LabelGroups) : [];
        return new LabelPickerViewModel(LiveLabels(), groups, currentLabels, pick);
    }

    async Task<TaskUndoEntry?> IMoveAndLabelHost.EditManyAsync(IReadOnlyList<Guid> ids, TaskBatchEdit edit, string toast)
    {
        if (_actions is null)
        {
            return null;
        }

        Notice = string.Empty;
        // A tag typed here that is hidden on this device is joined, as the right-click menu's New tag… does.
        foreach (string tag in edit.AddTags)
        {
            await ScopeNewTagAsync(tag, onlyHere: false);
        }

        return await _actions.EditManyAsync(ids, edit, toast);
    }

    // The label as the dialog shows it: its full name, its name under its category ("AI" under Work), its colour.
    private MoveAndLabelLabel LookOf(TaskLabel label)
    {
        string? category = null;
        if (_store is { IsActive: true } store)
        {
            IReadOnlyDictionary<Guid, TaskLabelGroup> groups = TaskLabelGroupBook.LiveById(TaskLabelGroupBook.Live(store.Lists.LabelGroups));
            category = TaskLabelGroupBook.GroupOf(label, groups)?.Name;
        }

        Color color = Color.TryParse(TaskLabelColors.Opaque(label.Color), out Color parsed) ? parsed : Colors.Transparent;
        return new MoveAndLabelLabel(label.Id, label.Name, TaskLabelShortNames.Of(label.Name, category), color);
    }
}
