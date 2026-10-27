using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Pick 3 for this session": a real session started with more than <see cref="SessionTasksService.MaxTasks"/> tasks.
/// The first three in list order start ticked; Keep needs exactly three, and the others are parked as come-backs. There is
/// no way to skip it.
/// </summary>
public sealed partial class PickTasksViewModel : ObservableObject
{
    private readonly SessionTasksService _tasks;

    public PickTasksViewModel(SessionTasksService tasks)
    {
        _tasks = tasks;
        int index = 0;
        foreach (SessionTask task in tasks.PickCandidates)
        {
            var row = new PickTaskRowViewModel(task.Id, task.Title, index++ < SessionTasksService.MaxTasks);
            row.PropertyChanged += (_, _) => Recount();
            Rows.Add(row);
        }

        Recount();
    }

    public ObservableCollection<PickTaskRowViewModel> Rows { get; } = new();

    public string Heading => $"Pick {SessionTasksService.MaxTasks} for this session";

    /// <summary>E.g. "Picked 3 of 3. The other 4 go to Come back to."</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>True once the prompt is answered; the window may close only then.</summary>
    public bool IsDone { get; private set; }

    public event EventHandler? Finished;

    /// <summary>The session ended before the pick: the prompt goes away and nothing is parked.</summary>
    public void Dismiss()
    {
        if (IsDone)
        {
            return;
        }

        IsDone = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void Recount()
    {
        int picked = Rows.Count(row => row.IsPicked);
        Status = $"Picked {picked} of {SessionTasksService.MaxTasks}. The other {Rows.Count - picked} go to Come back to.";
        KeepCommand.NotifyCanExecuteChanged();
    }

    private bool CanKeep() => Rows.Count(row => row.IsPicked) == SessionTasksService.MaxTasks;

    [RelayCommand(CanExecute = nameof(CanKeep))]
    private async Task KeepAsync()
    {
        await _tasks.KeepOnlyAsync(Rows.Where(row => row.IsPicked).Select(row => row.Id).ToHashSet());
        IsDone = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
