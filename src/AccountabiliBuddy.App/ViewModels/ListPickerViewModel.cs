using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "From your lists": the Tasks page's tasks, wherever a task is typed: the open items of Later, Come back to, Unfinished and Auto-saved
/// (newest first), then Done today, and the Archive once something is searched for. Two ways in: the searchable picker behind the button,
/// and <see cref="Suggestions"/> under the box while typing. Choosing one hands it to the owner's <c>pull</c>; a finished task comes back as
/// an open copy and its record stays. Ticked Later items are not listed. Task text is never logged.
/// </summary>
public sealed partial class ListPickerViewModel : ObservableObject
{
    private const int MinTypedForSuggestions = 2;
    private const int MaxSuggestions = 5;

    // The Archive can hold 90 days: a search shows the newest of what matches.
    private const int MaxArchiveRows = 30;

    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;
    private readonly Func<PulledListKind, Guid, string, Task> _pull;

    // A chosen task's title put in the box: not something to suggest for.
    private string? _filledTitle;
    private int _selected = -1;

    /// <param name="pull">What choosing an item does: the list it is in, its id and its text.</param>
    public ListPickerViewModel(SessionTasksService tasks, TimeProvider time, Func<PulledListKind, Guid, string, Task> pull)
    {
        _tasks = tasks;
        _time = time;
        _pull = pull;
        Search = string.Empty;
        Rows = [];
        Suggestions = [];
    }

    /// <summary>What the user typed to narrow the list down (the text, the category, a task's steps and note).</summary>
    [ObservableProperty]
    public partial string Search { get; set; }

    /// <summary>List names and their items in the order shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ListPickRowViewModel> Rows { get; set; }

    /// <summary>Nothing to show: every list is empty, or the search matches nothing.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>Tasks matching what is being typed in the box, shown under it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial IReadOnlyList<ListPickRowViewModel> Suggestions { get; set; }

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Raised when an item was chosen, so the view closes the picker.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when an item was chosen, so the box can empty (see <c>TaskSuggestions</c>).</summary>
    public event EventHandler? Picked;

    partial void OnSearchChanged(string value)
    {
        Refresh();
    }

    /// <summary>Called as the picker opens: clears the search and reads the lists again.</summary>
    public void Open()
    {
        if (Search.Length > 0)
        {
            Search = string.Empty; // Refresh runs from the change.
            return;
        }

        Refresh();
    }

    /// <summary>The box's text changed: suggests from 2 characters on.</summary>
    public void Typed(string text)
    {
        string typed = text.Trim();
        if (typed.Length < MinTypedForSuggestions || typed == _filledTitle)
        {
            DismissSuggestions();
            return;
        }

        _filledTitle = null;
        _selected = -1;
        Suggestions = BuildRows(typed).Where(row => row.IsItem).Take(MaxSuggestions).ToList();
    }

    public void DismissSuggestions()
    {
        _selected = -1;
        if (Suggestions.Count > 0)
        {
            Suggestions = [];
        }
    }

    /// <summary>Up or Down in the box. Above the first suggestion nothing is selected, so Enter adds what was typed.</summary>
    /// <returns>False with no suggestions showing.</returns>
    public bool MoveSelection(int by)
    {
        if (Suggestions.Count == 0)
        {
            return false;
        }

        _selected = Math.Clamp(_selected + by, -1, Suggestions.Count - 1);
        for (int index = 0; index < Suggestions.Count; index++)
        {
            Suggestions[index].IsSelected = index == _selected;
        }

        return true;
    }

    /// <summary>Enter in the box: picks the selected suggestion.</summary>
    /// <returns>False when none is selected: Enter adds what was typed.</returns>
    public bool PickSelected()
    {
        if (_selected < 0 || _selected >= Suggestions.Count)
        {
            return false;
        }

        Suggestions[_selected].PickCommand.Execute(null);
        return true;
    }

    /// <summary>A title the owner is about to put in the box, so it doesn't suggest itself.</summary>
    public void WillFill(string title)
    {
        _filledTitle = title.Trim();
    }

    private void Refresh()
    {
        List<ListPickRowViewModel> rows = BuildRows(Search.Trim());
        Rows = rows;
        IsEmpty = rows.Count == 0;
        EmptyText = Search.Trim().Length > 0 ? "Nothing matches." : "Nothing in your lists.";
    }

    private List<ListPickRowViewModel> BuildRows(string search)
    {
        DateOnly today = LocalDay(_time.GetUtcNow());
        var rows = new List<ListPickRowViewModel>();

        IReadOnlyList<SyncLaterItem> later = (_tasks.Later?.Items ?? [])
            .Where(item => Matches(search, item.Title, item.Category, item.Note, (item.Subtasks ?? []).Select(step => step.Title)))
            .OrderByDescending(item => item.EditedAt)
            .ToList();
        AddSection(rows, "Later", later.Select(item => Row(item.Title, LaterDetail(item, today), item.Category, item.CategoryColor, PulledListKind.Later, item.Id, "Later")));

        IReadOnlyList<Comeback> comebacks = (_tasks.Comebacks?.Open ?? [])
            .Where(item => Matches(search, item.Text, item.Category, null, []))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
        AddSection(rows, "Come back to", comebacks.Select(item =>
            Row(item.Text, $"Parked {DayText(LocalDay(item.CreatedAt), today)}", item.Category, item.CategoryColor, PulledListKind.ComeBack, item.Id, "Come back to")));

        // Unfinished from past sessions: the same two weeks the Tasks page lists.
        DateTimeOffset unfinishedFrom = _time.GetUtcNow() - TasksViewModel.PastShown;
        IReadOnlyList<PastTask> unfinished = _tasks.PastUnfinished()
            .Where(item => item.SessionStart >= unfinishedFrom && MatchesTask(search, item.Task))
            .OrderByDescending(item => item.SessionStart)
            .ToList();
        AddSection(rows, "Unfinished", unfinished.Select(item =>
            Row(item.Task.Title, $"Session of {DayText(LocalDay(item.SessionStart), today)}", CategoryNameOf(item.Task), item.Task.CategoryColor, PulledListKind.Unfinished, item.Task.Id, "Unfinished")));

        IReadOnlyList<AutoSavedTask> autoSaved = (_tasks.AutoSaved?.Items ?? [])
            .Where(item => Matches(search, item.Title, null, null, []))
            .OrderByDescending(item => item.SavedAt)
            .ToList();
        AddSection(rows, "Auto-saved", autoSaved.Select(item =>
            Row(item.Title, $"Saved {DayText(LocalDay(item.SavedAt), today)}", null, null, PulledListKind.AutoSaved, item.Id, "Auto-saved")));

        // Finished tasks come back as open copies. From the ended sessions' records: a task ticked in the running checklist is still in it.
        List<SessionTask> done = _tasks.History.SelectMany(checklist => checklist.Tasks)
            .Where(task => task.DoneAt is DateTimeOffset at && LocalDay(at) == today && MatchesTask(search, task))
            .GroupBy(task => task.Id).Select(group => group.First())
            .OrderByDescending(task => task.DoneAt)
            .ToList();
        AddSection(rows, "Done today", done.Select(task =>
            Row(task.Title, $"Finished {TimeZoneInfo.ConvertTime(task.DoneAt!.Value, _time.LocalTimeZone):t}", CategoryNameOf(task), task.CategoryColor, PulledListKind.DoneToday, task.Id, "Done today")));

        // The Archive only answers a search, so the list stays short.
        if (search.Length > 0)
        {
            HashSet<Guid> listed = [.. unfinished.Select(item => item.Task.Id), .. done.Select(task => task.Id)];
            List<SessionTask> archive = _tasks.History
                .OrderByDescending(checklist => checklist.SessionStart ?? DateTimeOffset.MinValue)
                .SelectMany(checklist => checklist.Tasks)
                .Where(task => !listed.Contains(task.Id) && MatchesTask(search, task))
                .GroupBy(task => task.Id).Select(group => group.First())
                .Take(MaxArchiveRows)
                .ToList();
            AddSection(rows, "Archive", archive.Select(task =>
                Row(task.Title, task.DoneAt is DateTimeOffset at ? $"Finished {DayText(LocalDay(at), today)}" : string.Empty, CategoryNameOf(task), task.CategoryColor, PulledListKind.Archive, task.Id, "Archive")));
        }

        return rows;
    }

    private static void AddSection(List<ListPickRowViewModel> rows, string name, IEnumerable<ListPickRowViewModel> items)
    {
        List<ListPickRowViewModel> section = items.ToList();
        if (section.Count > 0)
        {
            rows.Add(new ListPickRowViewModel(name));
            rows.AddRange(section);
        }
    }

    private ListPickRowViewModel Row(string title, string detail, string? category, string? categoryColor, PulledListKind kind, Guid id, string listName)
    {
        return new ListPickRowViewModel(title, detail, BrushOf(category, categoryColor), category, () => Choose(kind, id, title), listName);
    }

    private DateOnly LocalDay(DateTimeOffset time)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime);
    }

    // A record's category name: the one it kept, else this PC's category of that id.
    private string? CategoryNameOf(SessionTask task)
    {
        return task.Category ?? _tasks.Categories.FirstOrDefault(category => category.Id == task.CategoryId)?.Name;
    }

    private bool MatchesTask(string search, SessionTask task)
    {
        return Matches(search, task.Title, CategoryNameOf(task), task.Note, task.Subtasks.Select(step => step.Title));
    }

    private Task Choose(PulledListKind kind, Guid id, string title)
    {
        Closed?.Invoke(this, EventArgs.Empty);
        Picked?.Invoke(this, EventArgs.Empty);
        DismissSuggestions();
        return _pull(kind, id, title);
    }

    private static bool Matches(string search, string title, string? category, string? note, IEnumerable<string> steps)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || (category?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (note?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || steps.Any(step => step.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    // This PC's colour for a category of that name, else the colour the item carries; null for no category.
    private Avalonia.Media.IBrush? BrushOf(string? categoryName, string? storedColor)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        string? color = _tasks.Categories.FirstOrDefault(item => string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase))?.Color ?? storedColor;
        return TaskCategoryBrushes.Of(color);
    }

    private static string LaterDetail(SyncLaterItem item, DateOnly today)
    {
        var parts = new List<string>();
        if (item.DueOn is DateOnly due)
        {
            parts.Add($"Due {DayText(due, today)}");
        }

        // Only the open steps arrive when the item is taken (Add to Today drops the ticked ones), so only they are counted.
        int openSteps = (item.Subtasks ?? []).Count(step => !step.IsDone);
        if (openSteps > 0)
        {
            parts.Add(openSteps == 1 ? "1 step" : $"{openSteps} steps");
        }

        return string.Join(" · ", parts);
    }

    private static string DayText(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return "today";
        }

        if (day == today.AddDays(1))
        {
            return "tomorrow";
        }

        if (day == today.AddDays(-1))
        {
            return "yesterday";
        }

        return day.ToString(day.Year == today.Year ? "MMM d" : "MMM d, yyyy");
    }
}
