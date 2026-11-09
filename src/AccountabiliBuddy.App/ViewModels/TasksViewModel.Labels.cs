using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// ADR 72 on the Tasks page: a row's labels (plain names, several per task) and "Keep on…" (Synced, or local to one device).
public sealed partial class TasksViewModel
{
    private Func<string, Task>? _applyNewLabel;

    /// <summary>The other devices of the sync group a local item can be kept on (id and name; the watch never); set by the app.</summary>
    public Func<IReadOnlyList<(Guid Id, string Name)>>? KeepOnDevices { get; set; }

    /// <summary>This PC's id in its sync group; null while it isn't in one. Set by the app.</summary>
    public Func<Guid?>? SelfId { get; set; }

    /// <summary>Makes an item local to a device, or synced again with null (<see cref="TaskListSync.SetLocalAsync"/>); set by the app.</summary>
    public Func<LocalItemKind, Guid, Guid?, Task<bool>>? SetLocal { get; set; }

    /// <summary>The name box's hint: a new category's or a new label's name.</summary>
    public string NamePrompt => _applyNewLabel is null ? "New category name (Enter)" : "New label name (Enter)";

    // The ⋯ menu's "Labels": every label on this device's lists, ticked where the row has it (a click adds or removes it), and
    // "New label…". Not for another PC's past task: its record is that PC's.
    private List<TaskMenuEntry>? LabelsEntries(TaskRowKey key)
    {
        if (!CanLabel(key))
        {
            return null;
        }

        IReadOnlyList<string>? current = LabelsOf(key);
        var entries = new List<TaskMenuEntry>();
        foreach (string label in _tasks.AllLabels())
        {
            string name = label;
            bool has = TaskLabels.Has(current, name);
            entries.Add(new TaskMenuEntry(
                name,
                new AsyncRelayCommand(() => SetRowLabelsAsync(key, has ? TaskLabels.Without(LabelsOf(key), name) : TaskLabels.With(LabelsOf(key), [name]))),
                IsChecked: has));
        }

        entries.Add(TaskMenuEntry.Do("New label…", () => AskNewLabel(name => SetRowLabelsAsync(key, TaskLabels.With(LabelsOf(key), [name]))), MaterialIconKind.Plus));
        return entries;
    }

    // The ⋯ menu's "Keep on…": synced on every device, or local to this PC or another device of the group. Not for a task of the
    // running checklist, and not while this PC is in no group.
    private List<TaskMenuEntry>? KeepOnEntries(TaskRowKey key)
    {
        if (SetLocal is null || SelfId?.Invoke() is not Guid self || LocalKindOf(key) is not LocalItemKind kind)
        {
            return null;
        }

        Guid? keeper = LocalToOf(key);
        var entries = new List<TaskMenuEntry>
        {
            new("All my devices (synced)", new AsyncRelayCommand(() => SetLocalAsync(kind, key.Id, null)), IsChecked: keeper is null),
            new("Only this PC", new AsyncRelayCommand(() => SetLocalAsync(kind, key.Id, self)), IsChecked: keeper == self),
        };
        foreach ((Guid id, string name) in KeepOnDevices?.Invoke() ?? [])
        {
            Guid device = id;
            entries.Add(new TaskMenuEntry($"Only {name}", new AsyncRelayCommand(() => SetLocalAsync(kind, key.Id, device)), IsChecked: keeper == device));
        }

        return entries;
    }

    /// <summary>"#home #work" for a row's labels; empty for none.</summary>
    internal string LabelsText(IReadOnlyList<string>? labels)
    {
        return labels is { Count: > 0 } ? string.Join(" ", labels.Select(label => "#" + label)) : string.Empty;
    }

    private async Task SetLocalAsync(LocalItemKind kind, Guid id, Guid? keeper)
    {
        if (SetLocal is null)
        {
            return;
        }

        if (!await SetLocal(kind, id, keeper))
        {
            Notice = "That can't be kept on one device right now.";
            return;
        }

        Notice = keeper is null
            ? "It syncs to all your devices again."
            : keeper == SelfId?.Invoke() ? "Kept on this PC only." : "Sent to that device: it's kept there only, once it syncs.";
        RebuildLists();
    }

    private bool CanLabel(TaskRowKey key)
    {
        return key.Kind switch
        {
            TaskRowKind.Later or TaskRowKind.ComeBack or TaskRowKind.AutoSaved or TaskRowKind.Session => true,
            TaskRowKind.Past or TaskRowKind.Done => _tasks.Current.Tasks.Any(task => task.Id == key.Id) || _tasks.HistoryTaskOf(key.Id) is not null,
            _ => false,
        };
    }

    private IReadOnlyList<string>? LabelsOf(TaskRowKey key)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _later.Find(key.Id)?.Labels,
            TaskRowKind.ComeBack => _comebacks.Find(key.Id)?.Labels,
            TaskRowKind.AutoSaved => _autoSaved.Find(key.Id)?.Labels,
            _ => FindTask(key.Id)?.Labels ?? _tasks.PastTaskOf(key.Id)?.Labels,
        };
    }

    private Task SetRowLabelsAsync(TaskRowKey key, IReadOnlyList<string>? labels)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _later.EditAsync(key.Id, item => item with { Labels = TaskLabels.Clean(labels) }),
            TaskRowKind.ComeBack => _comebacks.SetLabelsAsync(key.Id, labels),
            TaskRowKind.AutoSaved => _autoSaved.SetLabelsAsync(key.Id, labels),
            _ => _tasks.SetLabelsAsync(key.Id, labels),
        };
    }

    private LocalItemKind? LocalKindOf(TaskRowKey key)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => LocalItemKind.Later,
            TaskRowKind.ComeBack => LocalItemKind.ComeBack,
            TaskRowKind.AutoSaved => LocalItemKind.AutoSaved,
            TaskRowKind.Session when _tasks.IsDraftTask(key.Id) => LocalItemKind.NextTask,
            TaskRowKind.Past or TaskRowKind.Done when _tasks.PastTaskOf(key.Id) is not null && !_tasks.Current.Tasks.Any(task => task.Id == key.Id) => LocalItemKind.PastTask,
            _ => null,
        };
    }

    private Guid? LocalToOf(TaskRowKey key)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _later.Find(key.Id)?.LocalTo,
            TaskRowKind.ComeBack => _comebacks.Find(key.Id)?.LocalTo,
            TaskRowKind.AutoSaved => _autoSaved.Find(key.Id)?.LocalTo,
            _ => FindTask(key.Id)?.LocalTo ?? _tasks.PastTaskOf(key.Id)?.LocalTo,
        };
    }

    // "New label…": the name box takes the keyboard; Enter applies the label to what the picker was for.
    private void AskNewLabel(Func<string, Task> apply)
    {
        _applyNewCategory = null;
        _applyNewLabel = apply;
        NewCategoryName = string.Empty;
        IsNamingCategory = true;
        OnPropertyChanged(nameof(NamePrompt));
        FocusRequested?.Invoke(this, "newCategory");
    }

    // Enter in the name box while it asks for a label: true when it was a label (and it was applied).
    private async Task<bool> SaveNewLabelAsync(string name)
    {
        Func<string, Task>? apply = _applyNewLabel;
        if (apply is null)
        {
            return false;
        }

        _applyNewLabel = null;
        OnPropertyChanged(nameof(NamePrompt));
        if (TaskLabels.Clean([name]) is [string label])
        {
            await apply(label);
        }

        return true;
    }
}
