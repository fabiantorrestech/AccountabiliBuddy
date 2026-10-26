using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Notebook tab: everything written down so it can leave your head. Come-backs (open and ticked), notes kept in
/// Focus mode, tasks left unfinished when a session ended, and tasks done, newest first, with their times; searchable
/// and filtered by kind. Open come-backs can be ticked off here; come-backs, notes and tasks can be edited or deleted.
/// </summary>
public sealed partial class NotebookViewModel : ObservableObject
{
    private readonly ComebackStore _comebacks;
    private readonly SessionNotesStore _notes;
    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;

    public NotebookViewModel(ComebackStore comebacks, SessionNotesStore notes, SessionTasksService tasks, TimeProvider time)
    {
        _comebacks = comebacks;
        _notes = notes;
        _tasks = tasks;
        _time = time;
        _comebacks.Changed += (_, _) => Refresh();
        _notes.Changed += (_, _) => Refresh();
        _tasks.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The kinds to show: "Everything" or one kind.</summary>
    public static IReadOnlyList<string> Filters { get; } = ["Everything", "Come-backs", "Notes", "Unfinished tasks", "Done tasks"];

    [ObservableProperty]
    public partial string Filter { get; set; } = "Everything";

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public ObservableCollection<NotebookItemViewModel> Items { get; } = new();

    /// <summary>E.g. "3 open come-backs · 12 notes · 4 unfinished · 31 done".</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>Builds the list again from the stores.</summary>
    public void Refresh()
    {
        List<NotebookItemViewModel> all = [.. Comebacks(), .. Notes(), .. Tasks()];
        int open = _comebacks.All.Count(comeback => comeback.DoneAt is null);
        SummaryText = $"{open} open come-backs · {_notes.All.Count} notes · {all.Count(item => item.Kind == UnfinishedKind)} unfinished · {all.Count(item => item.Kind == DoneKind)} done";

        string search = Search.Trim();
        IEnumerable<NotebookItemViewModel> shown = all
            .Where(item => Filter switch
            {
                "Come-backs" => item.Kind == ComebackKind,
                "Notes" => item.Kind == NoteKind,
                "Unfinished tasks" => item.Kind is UnfinishedKind or UnfinishedSubtaskKind,
                "Done tasks" => item.Kind is DoneKind or DoneSubtaskKind,
                _ => true,
            })
            .Where(item => search.Length == 0 || item.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Sort);
        Items.Clear();
        foreach (NotebookItemViewModel item in shown)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private const string ComebackKind = "Come-back";
    private const string NoteKind = "Note";
    private const string UnfinishedKind = "Unfinished task";
    private const string DoneKind = "Done task";
    private const string UnfinishedSubtaskKind = "Unfinished subtask";
    private const string DoneSubtaskKind = "Done subtask";

    private IEnumerable<NotebookItemViewModel> Comebacks()
    {
        return _comebacks.All.Select(comeback => new NotebookItemViewModel(
            ComebackKind,
            comeback.Text,
            comeback.DoneAt is DateTimeOffset done ? $"Parked {When(comeback.CreatedAt)} · ticked {When(done)}" : $"Parked {When(comeback.CreatedAt)} · open",
            comeback.CreatedAt,
            comeback.DoneAt is null ? new AsyncRelayCommand(() => _comebacks.TickAsync(comeback.Id)) : null,
            rename: text => _comebacks.EditAsync(comeback.Id, text),
            delete: () => _comebacks.DeleteAsync(comeback.Id)));
    }

    private IEnumerable<NotebookItemViewModel> Notes()
    {
        return _notes.All.Select(note => new NotebookItemViewModel(
            NoteKind,
            note.Text,
            $"Noted {When(note.At)}",
            note.At,
            rename: text => _notes.EditAsync(note.Id, text),
            delete: () => _notes.DeleteAsync(note.Id)));
    }

    // Ended sessions' checklists: what was done, and what was left open when the session ended. Each task is followed by
    // its subtasks (set in, just after it), and both can be renamed or deleted.
    private IEnumerable<NotebookItemViewModel> Tasks()
    {
        foreach (SessionChecklist checklist in _tasks.History.Where(checklist => checklist.SessionStart is not null))
        {
            DateTimeOffset start = checklist.SessionStart!.Value;
            foreach (SessionTask task in checklist.Tasks)
            {
                DateTimeOffset sort = task.DoneAt ?? start;
                // Its category as it was when the session ended, e.g. "Work · Session of …".
                string category = CategoryName(task) is string name ? $"{name} · " : string.Empty;
                yield return new NotebookItemViewModel(
                    task.DoneAt is null ? UnfinishedKind : DoneKind,
                    task.Title,
                    task.DoneAt is DateTimeOffset done
                        ? $"{category}Session of {When(start)} · done {When(done)}"
                        : $"{category}Session of {When(start)} · left open",
                    sort,
                    rename: title => _tasks.EditHistoryAsync(task.Id, title),
                    delete: () => _tasks.EditHistoryAsync(task.Id, null));
                int order = 0;
                foreach (SessionSubtask subtask in task.Subtasks)
                {
                    // Just after its task in the newest-first list.
                    order++;
                    yield return new NotebookItemViewModel(
                        subtask.DoneAt is null ? UnfinishedSubtaskKind : DoneSubtaskKind,
                        subtask.Title,
                        subtask.DoneAt is DateTimeOffset subDone ? $"Done {When(subDone)}" : "Left open",
                        sort - TimeSpan.FromTicks(order),
                        rename: title => _tasks.EditHistoryAsync(subtask.Id, title),
                        delete: () => _tasks.EditHistoryAsync(subtask.Id, null),
                        isSubtask: true);
                }
            }
        }
    }

    private string? CategoryName(SessionTask task)
    {
        return task.Category ?? _tasks.Categories.FirstOrDefault(category => category.Id == task.CategoryId)?.Name;
    }

    private string When(DateTimeOffset time)
    {
        DateTime local = TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime;
        return $"{local.ToString("ddd", CultureInfo.CurrentCulture)} {local:M/d} {local:t}";
    }
}
