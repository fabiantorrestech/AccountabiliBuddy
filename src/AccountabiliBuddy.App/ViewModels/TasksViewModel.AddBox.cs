using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The page's add boxes (spec 03 §B, B2): the Today column's "+ Add a task", each Shelf bin's "+" and Jot it down, and what they need
/// (<see cref="IAddBoxHost"/>). A task is added through the task list's actions (number, stamps, undo, sync); This session is Queued
/// first and then offered to the session's own add, so the cap decides and a full session leaves it in Queued. While the task list is
/// not the source (or can't be opened), the old lists take the add as the old add row did, so an unreadable list still refuses it and
/// says so (<c>AddRefused</c>).
/// </summary>
public sealed partial class TasksViewModel : IAddBoxHost
{
    private AddBoxViewModel? _addBox;
    private AddBoxViewModel? _jot;

    /// <summary>The Today column's add box ("+ Add a task", Send to To sort by default).</summary>
    public AddBoxViewModel AddBox => _addBox ??= new AddBoxViewModel(this, AddBoxKind.Main);

    /// <summary>Jot it down, shared by the Today page and the Today column (Ctrl+J).</summary>
    public AddBoxViewModel Jot => _jot ??= new AddBoxViewModel(this, AddBoxKind.Jot);

    /// <summary>This PC's Tasks settings (the user's project markers); set by the app. Null: no taught markers, nothing offered.</summary>
    public TaskSettingsService? TaskSettings { get; set; }

    /// <summary>Ctrl+J: the jot opens and takes the keyboard (the caller shows a page that has it first).</summary>
    public void OpenJot() => Jot.Open();

    IReadOnlyList<TaskLabel> IAddBoxHost.Labels => LiveLabels();

    IReadOnlyList<ProjectMarkerPattern> IAddBoxHost.CustomMarkers => TaskSettings?.Current.CustomMarkers ?? [];

    ListPickerViewModel? IAddBoxHost.FromYourLists => ListPicker;

    IReadOnlyList<KeyValuePair<string, int>> IAddBoxHost.TagCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (TaskItem task in LiveTasks())
        {
            foreach (string tag in task.Tags ?? [])
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    ProjectMarkerCandidate? IAddBoxHost.MarkerToOffer()
    {
        if (TaskSettings is not { } settings || _store is not { IsActive: true } store)
        {
            return null;
        }

        // Titles of the tasks made lately, live or in History; the learner keeps only the marker's characters.
        IEnumerable<(string, DateTimeOffset)> titles = store.Lists.Tasks.Select(item => item.Task).Where(task => task.IsLive)
            .Concat(store.Lists.Log.Where(item => item.Entry.IsLive).Select(item => item.Entry.Task))
            .Select(task => (task.Title, task.CreatedAt));
        IReadOnlyList<ProjectMarkerPattern> known = [.. settings.Current.CustomMarkers ?? [], .. settings.Current.DeclinedMarkers ?? []];
        return TaskMarkerLearner.Candidates(titles, known, _time.GetUtcNow()).FirstOrDefault();
    }

    Task IAddBoxHost.LearnMarkerAsync(ProjectMarkerPattern marker)
    {
        return TaskSettings?.LearnMarkerAsync(marker) ?? Task.CompletedTask;
    }

    Task IAddBoxHost.DeclineMarkerAsync(ProjectMarkerPattern marker)
    {
        return TaskSettings?.DeclineMarkerAsync(marker) ?? Task.CompletedTask;
    }

    async Task<Guid?> IAddBoxHost.CreateLabelAsync(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // A label is a category of the session list underneath (ADR 76): adopting the name makes it, in the next free colour.
        await _tasks.AdoptCategoryAsync(trimmed, null);
        return LiveLabels().FirstOrDefault(label => string.Equals(label.Name, trimmed, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    async Task<bool> IAddBoxHost.AddFromBoxAsync(AddBoxRequest request)
    {
        Notice = string.Empty;
        if (_actions is null || _store is not { IsActive: true })
        {
            await AddToOldListsAsync(request);
            return true;
        }

        // This session: into Queued first, then through the session's add (its cap); a full session leaves it in Queued.
        TaskBin bin = request.Destination is TaskBin.Session or TaskBin.Done ? TaskBin.Queued : request.Destination;
        Guid id = Guid.NewGuid();
        if (!await _actions.AddAsync(id, request.Title, bin, request.LabelId, request.Tags, request.Note))
        {
            return false;
        }

        if (request.Destination == TaskBin.Session)
        {
            TaskMoveOutcome outcome = await _actions.MoveToSessionAsync([id]);
            if (outcome.NeedsRoom.Count > 0)
            {
                // It waits in Queued (never To sort); "This session is full" offers a swap or "Add anyway".
                OfferSwap(outcome.NeedsRoom, fallback: "This session is full, so “" + TaskToasts.Short(request.Title) + "” went to Queued.");
            }
        }

        return true;
    }

    /// <summary>Today on the trusted clock (the jot's count).</summary>
    public DateOnly Today => DateOnly.FromDateTime(LocalNow());

    // Before the task list is the source, or while it can't be opened: the old lists, as the old add row and the Shelf's "+" did. A note
    // (the split's tail) is kept: on the session list's task, or folded into a come-back's text (a come-back has no note of its own).
    private async Task AddToOldListsAsync(AddBoxRequest request)
    {
        TaskLabel? label = LiveLabels().FirstOrDefault(candidate => candidate.Id == request.LabelId);
        string? color = label is null ? null : TaskLabelColors.Opaque(label.Color);
        IReadOnlyList<string>? tags = request.Tags.Count == 0 ? null : request.Tags;
        bool hasNote = !string.IsNullOrWhiteSpace(request.Note);
        switch (request.Destination)
        {
            case TaskBin.Sort:
                await _comebacks.AddAsync(hasNote ? request.Title + " — " + request.Note!.Trim() : request.Title, label?.Name, color, tags);
                break;
            case TaskBin.Session or TaskBin.Queued when !SessionIsFull:
                HashSet<Guid> before = _tasks.Current.Tasks.Select(task => task.Id).ToHashSet();
                await _tasks.AddAsync(request.Title, null, CategoryIdOf(request.LabelId));
                if (hasNote && _tasks.Current.Tasks.FirstOrDefault(task => !before.Contains(task.Id)) is { } added)
                {
                    await _tasks.SetNoteAsync(added.Id, request.Note);
                }

                break;
            default:
                await _later.AddAsync(request.Title, null, label?.Name, color, null, request.Note, null, labels: tags);
                break;
        }
    }

    private List<TaskLabel> LiveLabels()
    {
        return _store is { IsActive: true } store
            ? store.Lists.Labels.Select(item => item.Label).Where(label => label.IsLive).OrderBy(label => label.Position).ToList()
            : [];
    }

    private IEnumerable<TaskItem> LiveTasks()
    {
        return _store is { IsActive: true } store ? store.Lists.Tasks.Select(item => item.Task).Where(task => task.IsLive) : [];
    }
}
