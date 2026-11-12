using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks page's Today strip (round 2 R3, change note §2, board Tasks-PC): "Today · This session 3 ready · Queued 2 · Reminders due
/// today 1 · Finished 1", "Drag here, or hover a row and press ←", and Open Today. It only counts (no timer: the time lives in the title
/// bar); a Shelf row dropped on it goes to Today › Queued (<see cref="Services.Tasks.TodayStripDrop"/>, Undo in the toast). It lights up
/// while a row is dragged and highlights while one is over it.
/// </summary>
public sealed partial class TodayStripViewModel : ObservableObject
{
    private readonly Func<TaskListRowViewModel, Task> _drop;
    private readonly Action _openToday;

    public TodayStripViewModel(Func<TaskListRowViewModel, Task> drop, Action openToday)
    {
        _drop = drop;
        _openToday = openToday;
        CountsText = string.Empty;
    }

    /// <summary>"This session 3 ready · Queued 2 · Reminders due today 1 · Finished 1" (parts at 0 are left out, This session never).</summary>
    [ObservableProperty]
    public partial string CountsText { get; set; }

    public string HintText => "Drag here, or hover a row and press";

    /// <summary>A row is being dragged on the page: the strip shows it takes drops.</summary>
    [ObservableProperty]
    public partial bool IsLit { get; set; }

    /// <summary>A dragged row is over the strip: letting go drops it here.</summary>
    [ObservableProperty]
    public partial bool IsHot { get; set; }

    /// <summary>The counts from the page's rebuild.</summary>
    public void Update(string session, int queued, int reminders, int finished)
    {
        var parts = new List<string> { "This session " + session };
        if (queued > 0)
        {
            parts.Add("Queued " + queued);
        }

        if (reminders > 0)
        {
            parts.Add("Reminders due today " + reminders);
        }

        if (finished > 0)
        {
            parts.Add("Finished " + finished);
        }

        CountsText = string.Join(" · ", parts);
    }

    /// <summary>A row let go over the strip.</summary>
    public Task DropAsync(TaskListRowViewModel row) => _drop(row);

    /// <summary>Open Today (and a click on the strip): the window shows the Today tab.</summary>
    [RelayCommand]
    private void OpenToday() => _openToday();
}
