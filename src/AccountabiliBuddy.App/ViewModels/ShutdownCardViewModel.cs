using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The shutdown card (redesign phase 5, design J): at the end of the day's last work part, open come-backs to tick,
/// today's unfinished tasks to carry or drop, and tomorrow's first task. Done applies them; Skip closes it unchanged.
/// </summary>
public sealed partial class ShutdownCardViewModel : ObservableObject
{
    private readonly Func<ShutdownCardViewModel, Task> _apply;
    private readonly Func<Task> _close;

    /// <param name="apply">Writes the choices and closes the card (set by the service).</param>
    /// <param name="close">Closes it without changes.</param>
    public ShutdownCardViewModel(
        IReadOnlyList<ShutdownComebackRow> comebacks, IReadOnlyList<ShutdownTaskGroup> unfinished, Func<ShutdownCardViewModel, Task> apply, Func<Task> close, SessionTasksService? tasks = null)
    {
        TomorrowPicker = tasks is null ? null : new ListPickerViewModel(tasks, tasks.Clock, FillTomorrowAsync);
        Comebacks = comebacks;
        UnfinishedGroups = unfinished;
        _apply = apply;
        _close = close;
        TomorrowTask = string.Empty;
    }

    public IReadOnlyList<ShutdownComebackRow> Comebacks { get; }

    /// <summary>Today's unfinished tasks under their category, the ones with no category last.</summary>
    public IReadOnlyList<ShutdownTaskGroup> UnfinishedGroups { get; }

    public IEnumerable<ShutdownTaskRow> Unfinished => UnfinishedGroups.SelectMany(group => group.Rows);

    public int ComebackCount => Comebacks.Count;

    public int UnfinishedCount => Unfinished.Count();

    public bool HasComebacks => Comebacks.Count > 0;

    public bool HasUnfinished => UnfinishedGroups.Count > 0;

    /// <summary>Tomorrow's first task: it waits in Today's checklist, first.</summary>
    [ObservableProperty]
    public partial string TomorrowTask { get; set; }

    /// <summary>"From your lists" by the box; null without the tasks service.</summary>
    public ListPickerViewModel? TomorrowPicker { get; }

    /// <summary>The list task chosen for tomorrow: it moves there on Done. Null for a typed one; changing the text makes it typed.</summary>
    public (PulledListKind Kind, Guid Id)? TomorrowSource { get; private set; }

    private string? _tomorrowSourceTitle;

    partial void OnTomorrowTaskChanged(string value)
    {
        if (TomorrowSource is not null && value.Trim() != _tomorrowSourceTitle)
        {
            TomorrowSource = null;
            _tomorrowSourceTitle = null;
        }
    }

    private Task FillTomorrowAsync(PulledListKind kind, Guid id, string title)
    {
        _tomorrowSourceTitle = title.Trim();
        TomorrowPicker?.WillFill(title);
        TomorrowTask = title;
        TomorrowSource = (kind, id);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task DoneAsync()
    {
        return _apply(this);
    }

    [RelayCommand]
    private Task SkipAsync()
    {
        return _close();
    }
}
