using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "From your lists": a searchable picker of the open items of Later and Come back to (newest first), for the "Add a task" box on
/// Today, in the quick-session popup and in the start of your day. Choosing one hands it to the checklist, which adds it the way
/// "Add to Today" does on the Tasks page. Done items are not listed. Task text is never logged.
/// </summary>
public sealed partial class ListPickerViewModel : ObservableObject
{
    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;
    private readonly Func<PulledListKind, Guid, string, Task> _pull;

    /// <param name="pull">What choosing an item does: the list it is in, its id and its text.</param>
    public ListPickerViewModel(SessionTasksService tasks, TimeProvider time, Func<PulledListKind, Guid, string, Task> pull)
    {
        _tasks = tasks;
        _time = time;
        _pull = pull;
        Search = string.Empty;
        Rows = [];
    }

    /// <summary>What the user typed to narrow the list down (the text, the category, a Later item's steps and note).</summary>
    [ObservableProperty]
    public partial string Search { get; set; }

    /// <summary>List names and their items in the order shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ListPickRowViewModel> Rows { get; set; }

    /// <summary>Nothing to show: both lists are empty, or the search matches nothing.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>Raised when an item was chosen, so the view closes the picker.</summary>
    public event EventHandler? Closed;

    partial void OnSearchChanged(string value)
    {
        Refresh();
    }

    /// <summary>Called as the picker opens: clears the search and reads both lists again.</summary>
    public void Open()
    {
        if (Search.Length > 0)
        {
            Search = string.Empty; // Refresh runs from the change.
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        string search = Search.Trim();
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);
        var rows = new List<ListPickRowViewModel>();

        IReadOnlyList<SyncLaterItem> later = (_tasks.Later?.Items ?? [])
            .Where(item => Matches(search, item.Title, item.Category, item.Note, (item.Subtasks ?? []).Select(step => step.Title)))
            .OrderByDescending(item => item.EditedAt)
            .ToList();
        if (later.Count > 0)
        {
            rows.Add(new ListPickRowViewModel("Later"));
            foreach (SyncLaterItem item in later)
            {
                Guid id = item.Id;
                string title = item.Title;
                rows.Add(new ListPickRowViewModel(
                    title, LaterDetail(item, today), BrushOf(item.Category, item.CategoryColor), item.Category, () => Choose(PulledListKind.Later, id, title)));
            }
        }

        IReadOnlyList<Comeback> comebacks = (_tasks.Comebacks?.Open ?? [])
            .Where(item => Matches(search, item.Text, item.Category, null, []))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
        if (comebacks.Count > 0)
        {
            rows.Add(new ListPickRowViewModel("Come back to"));
            foreach (Comeback item in comebacks)
            {
                Guid id = item.Id;
                string text = item.Text;
                string parked = $"Parked {DayText(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.CreatedAt, _time.LocalTimeZone).DateTime), today)}";
                rows.Add(new ListPickRowViewModel(
                    text, parked, BrushOf(item.Category, item.CategoryColor), item.Category, () => Choose(PulledListKind.ComeBack, id, text)));
            }
        }

        Rows = rows;
        IsEmpty = rows.Count == 0;
        EmptyText = search.Length > 0 ? "Nothing matches." : "Nothing in Later or Come back to.";
    }

    private Task Choose(PulledListKind kind, Guid id, string title)
    {
        Closed?.Invoke(this, EventArgs.Empty);
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
