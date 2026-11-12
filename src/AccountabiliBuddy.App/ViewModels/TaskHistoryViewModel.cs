using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// History (spec 05 §10, from the View menu): the last 30 days of the task list, newest first, in two lists: Finished (Do again: a new
/// task in Queued) and Deleted and archived (Bring back: the same task to the bin it left, never This session). Both go through
/// <see cref="TaskActionsService"/>, so they are undoable and toasted. Titles are the user's text: shown only. Times are 12-hour.
/// </summary>
public sealed partial class TaskHistoryViewModel : ObservableObject
{
    private readonly TaskActionsService _actions;
    private readonly TimeProvider _time;

    public TaskHistoryViewModel(TaskActionsService actions, TimeProvider time)
    {
        _actions = actions;
        _time = time;
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFinished), nameof(IsEmpty))]
    public partial IReadOnlyList<TaskHistoryRowViewModel> Finished { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoved), nameof(IsEmpty))]
    public partial IReadOnlyList<TaskHistoryRowViewModel> Removed { get; set; } = [];

    public bool HasFinished => Finished.Count > 0;

    public bool HasRemoved => Removed.Count > 0;

    public bool IsEmpty => !HasFinished && !HasRemoved;

    public void Refresh()
    {
        TaskHistoryList list = _actions.History();
        Finished = list.Finished.Select(entry => RowOf(entry, "Finished ", "Do again", MaterialIconKind.Replay, () => _actions.DoAgainAsync(entry.Id))).ToList();
        Removed = list.Removed.Select(entry => RowOf(
                entry, entry.Kind == TaskHistoryKind.Archived ? "Archived " : "Deleted ", "Bring back", MaterialIconKind.ArrowULeftTop,
                () => _actions.BringBackAsync(entry.Id)))
            .ToList();
    }

    private TaskHistoryRowViewModel RowOf(TaskHistoryEntry entry, string what, string action, MaterialIconKind icon, Func<Task> run)
    {
        DateTime local = TimeZoneInfo.ConvertTime(entry.When, _time.LocalTimeZone).DateTime;
        string when = local.ToString("ddd M/d", CultureInfo.InvariantCulture) + " " + TaskSnooze.Clock(TimeOnly.FromDateTime(local));
        return new TaskHistoryRowViewModel(entry.Task.Title, what + when, action, icon, new AsyncRelayCommand(async () =>
        {
            await run();
            Refresh();
        }));
    }
}
