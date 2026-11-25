using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign item 5 (SPEC "Task editor popup", boards 06, 08, 33): one centred popup is the only task editor. It shows the open task
// (Detail, while IsDetailOpen) or a new task from the add box's Details (Draft), over the page that opened it: the Tasks page, or Today
// when a row was opened there (Today has the same popup; it no longer switches to Tasks). The bin pill moves the open task at once
// (into This session only through the session's cap), and the editor follows the task into its new list.
public sealed partial class TasksViewModel
{
    private TaskDraftViewModel? _draft;
    private bool _editorOnToday;

    /// <summary>The new task being filled in the popup (the add box's Details); null when none.</summary>
    public TaskDraftViewModel? Draft => _draft;

    /// <summary>What the popup shows: the new task, else the open task; null while it is closed.</summary>
    public object? Editor => _draft is not null ? _draft : IsDetailOpen ? Detail : null;

    public bool ShowsEditor => Editor is not null;

    /// <summary>The popup belongs to the Today page (a row opened there); a new task's popup is always the Tasks page's.</summary>
    public bool IsEditorOnToday => _editorOnToday && _draft is null;

    /// <summary>The Tasks page shows the popup.</summary>
    public bool ShowsEditorOnTasks => ShowsEditor && !IsEditorOnToday;

    /// <summary>The Today page shows the popup.</summary>
    public bool ShowsEditorOnToday => ShowsEditor && IsEditorOnToday;

    /// <summary>The Today page is on screen (set by its view). Leaving it closes a task opened there.</summary>
    public bool IsTodayOnScreen
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            if (!value && IsEditorOnToday && IsDetailOpen)
            {
                CloseDetail();
            }
        }
    }

    partial void OnDetailChanged(TaskDetailViewModel? value) => RaiseEditor();

    // A row opened on the Today page (Tasks not showing) opens the popup there; anywhere else the window shows Tasks.
    private void ShowEditorWhereOpened()
    {
        ShowEditorOn(onTasks: !(IsTodayOnScreen && !IsOnScreen));
    }

    private void ShowEditorOn(bool onTasks)
    {
        _editorOnToday = !onTasks;
        RaiseEditor();
        if (onTasks)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseEditor()
    {
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(IsEditorOnToday));
        OnPropertyChanged(nameof(ShowsEditorOnTasks));
        OnPropertyChanged(nameof(ShowsEditorOnToday));
        if (_addBox is not null)
        {
            _addBox.IsDetailsOpen = _draft is not null;
        }
    }

    /// <summary>The × and a click on the dim: the open task closes (what is typed is kept), or a new task goes back to the add box.</summary>
    [RelayCommand]
    private void CloseEditor()
    {
        if (_draft is not null)
        {
            CancelDraft();
            return;
        }

        CloseDetail();
    }

    // ---- The open task's pills ----

    /// <summary>The open task's bin: the task list's, else the list its row belongs to.</summary>
    internal TaskBin BinOf(TaskRowKey key, TaskItem? item)
    {
        if (item is not null)
        {
            return item.Bin;
        }

        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.IsLive ? TaskBin.Session : TaskBin.Queued,
            TaskRowKind.ComeBack or TaskRowKind.AutoSaved => TaskBin.Sort,
            TaskRowKind.Later => _later.Items.FirstOrDefault(later => later.Id == key.Id)?.Bin == SyncLaterItem.NextBin ? TaskBin.Next : TaskBin.Later,
            TaskRowKind.Queued => TaskBin.Queued,
            _ => TaskBin.Done,
        };
    }

    /// <summary>The bin pill moves the task: the task list holds it (a running session's own tasks leave only as the session allows).</summary>
    internal bool CanMoveBin(TaskRowKey key) => MenuTask(key) is not null;

    /// <summary>A bin picked on the open task's bin pill: it moves at once (This session through the session's cap, as Move to does).</summary>
    internal Task MoveToBinAsync(TaskRowKey key, TaskBin bin)
    {
        if (MenuTask(key) is not TaskItem item || item.Bin == bin)
        {
            return Task.CompletedTask;
        }

        return MoveManyAsync([key.Id], bin);
    }

    internal string? LabelNameOf(Guid? labelId) => LabelOf(labelId)?.Name;

    internal IBrush LabelBrushOf(Guid? labelId)
    {
        return LabelOf(labelId) is { } label ? TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color)) : Brushes.Transparent;
    }

    /// <summary>
    /// The label pill's picker (the shared LabelPicker: categories, search, No label) on <paramref name="current"/>; null before the task
    /// list exists (the pill then offers the older label menu).
    /// </summary>
    internal LabelPickerViewModel? LabelPickerOf(Guid? current)
    {
        if (_store is not { IsActive: true } store)
        {
            return null;
        }

        LabelPick initial = current is Guid id && LabelOf(id) is not null ? LabelPick.Of(id) : LabelPick.NoLabel;
        return new LabelPickerViewModel(LiveLabels(), TaskLabelGroupBook.Live(store.Lists.LabelGroups), [current], initial);
    }

    /// <summary>The open task's label picker; null where the task list doesn't hold it.</summary>
    internal LabelPickerViewModel? LabelPickerFor(TaskRowKey key)
    {
        return MenuTask(key) is TaskItem item ? LabelPickerOf(item.LabelId) : null;
    }

    /// <summary>A label (or No label) picked for the open task.</summary>
    internal Task ApplyLabelPickAsync(TaskRowKey key, LabelPick pick)
    {
        if (pick.IsNothing || MenuTask(key) is null)
        {
            return Task.CompletedTask;
        }

        Guid? labelId = pick.IsNoLabel ? null : pick.LabelId;
        return BulkAsync(actions => actions.SetLabelAsync([key.Id], labelId));
    }

    // ---- A new task (the add box's Details, Ctrl+Enter while typing) ----

    /// <summary>
    /// The add box's Details: the popup opens for a new task with what was typed (the title without its "@" / "#" words, the picked or
    /// typed label and tags) and Settings › Tasks' default time limit. Only the Tasks page's own box has Details.
    /// </summary>
    void IAddBoxHost.OpenDetails(AddBoxViewModel box)
    {
        if (box.Kind != AddBoxKind.Main || _draft is not null)
        {
            return;
        }

        TaskTypedTitle parsed = TaskTitleHelp.Parse(box.Text, LiveLabels());
        IReadOnlyList<string> tags = TaskLabels.With(box.Tags, parsed.Tags) ?? [];
        if (IsDetailOpen)
        {
            CloseDetail();
        }

        _draft = new TaskDraftViewModel(this, box, parsed.Title, box.LabelId ?? parsed.LabelId, tags, TaskSettings?.Current.NewTaskTimeLimit);
        _editorOnToday = false;
        RaiseEditor();
        FocusRequested?.Invoke(this, "draftTitle");
    }

    /// <summary>Cancel or Esc in the new task's popup: back to the add box, its text and chips as they were, the caret in it.</summary>
    internal void CancelDraft()
    {
        if (_draft is null)
        {
            return;
        }

        _draft.Dispose();
        _draft = null;
        RaiseEditor();
        AddBox.Open();
    }

    /// <summary>
    /// "Add to [bin]" or Ctrl+Enter in the new task's popup: the task is added with everything filled in as one action (Undo deletes it);
    /// This session goes to Queued first and then through the session's cap, as the add box does. The add box empties.
    /// </summary>
    internal async Task AddDraftAsync(TaskDraftViewModel draft)
    {
        string title = draft.Title.Trim();
        if (title.Length == 0)
        {
            FocusRequested?.Invoke(this, "draftTitle");
            return;
        }

        Notice = string.Empty;
        TaskBin destination = draft.Bin;
        string? note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note;
        if (_actions is null || _store is not { IsActive: true })
        {
            // Before the task list: the old lists take it as the add box's add does (title, bin, label, tags and note).
            await AddToOldListsAsync(new AddBoxRequest(title, destination, draft.LabelId, draft.Tags.ToList(), note));
        }
        else
        {
            TaskBin bin = destination is TaskBin.Session or TaskBin.Done ? TaskBin.Queued : destination;
            Guid id = Guid.NewGuid();
            var details = new TaskAddDetails(
                draft.Steps.Select(step => step.Title).ToList(), draft.IsHighlighted, draft.EstimateMinutes, draft.DueOn,
                ReminderSettings?.Current.NewRemindersTo ?? TaskCalendarDestination.GoogleCalendar);
            if (!await _actions.AddWithDetailsAsync(id, title, bin, draft.LabelId, draft.Tags.ToList(), note, details))
            {
                return;
            }

            if (destination == TaskBin.Session)
            {
                TaskMoveOutcome outcome = await _actions.MoveToSessionAsync([id]);
                if (outcome.NeedsRoom.Count > 0)
                {
                    OfferSwap(outcome.NeedsRoom, fallback: "This session is full, so “" + TaskToasts.Short(title) + "” went to Queued.");
                }
            }
        }

        if (!ReferenceEquals(_draft, draft))
        {
            return;
        }

        draft.Dispose();
        _draft = null;
        RaiseEditor();
        AddBox.ClearAfterDetails();
    }

    /// <summary>The new task's tags menu: the tags this device shows, ticked where picked, and "New tag…" (an inline box in the popup's tag pill area).</summary>
    internal IReadOnlyList<TaskMenuEntry> DraftTagMenu(IReadOnlyList<string> current, Action<string> toggle, Action<string> add)
    {
        IEnumerable<string> known = _store is { IsActive: true } store ? store.VisibleTags() : _tasks.AllLabels();
        var items = new List<(bool Shared, TaskMenuEntry Entry)>();
        foreach (string tag in known)
        {
            string name = tag;
            items.Add((IsSharedTag(name), new TaskMenuEntry(name, new RelayCommand(() => toggle(name)), IsChecked: TaskLabels.Has(current, name))));
        }

        List<TaskMenuEntry> entries = ScopeGrouped(items);
        entries.Add(InlineNewTagEntry(name =>
        {
            add(name);
            return Task.CompletedTask;
        }));
        return entries;
    }
}
