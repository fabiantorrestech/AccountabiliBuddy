using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Delete N tasks?" (spec 03 §C): the titles (the first few, then "and N more"), red Delete and Cancel. Delete runs the given deletion
/// (<see cref="TasksViewModel.DeleteManyAsync"/>: History as deleted, Bring back for 30 days, one undo step). Titles are shown only.
/// </summary>
public sealed partial class DeleteTasksViewModel : ObservableObject
{
    /// <summary>How many titles are listed before "and N more".</summary>
    public const int Listed = 5;

    private readonly Func<Task> _delete;

    public DeleteTasksViewModel(IReadOnlyList<string> titles, Func<Task> delete)
    {
        _delete = delete;
        Heading = titles.Count == 1 ? "Delete 1 task?" : $"Delete {titles.Count} tasks?";
        Titles = titles.Take(Listed).ToList();
        MoreText = titles.Count > Listed ? $"and {titles.Count - Listed} more" : string.Empty;
        DeleteText = titles.Count == 1 ? "Delete" : $"Delete {titles.Count}";
    }

    public event EventHandler? CloseRequested;

    public string Heading { get; }

    public IReadOnlyList<string> Titles { get; }

    public string MoreText { get; }

    public bool HasMore => MoreText.Length > 0;

    public string DeleteText { get; }

    public string Note => "They go to History as deleted: Bring back works there for 30 days, and Ctrl+Z undoes it.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsBusy { get; set; }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        IsBusy = true;
        try
        {
            await _delete();
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanDelete() => !IsBusy;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
