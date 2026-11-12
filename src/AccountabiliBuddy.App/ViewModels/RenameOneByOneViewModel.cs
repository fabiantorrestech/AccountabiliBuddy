using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Rename one by one" (spec 05 §12, prototype dialog "rename"): ‹ › through the picked tasks ("Rename 2 of 5"), the old name in italics
/// over the box, a progress strip; Enter goes to the next one, Alt+←/→ step. Nothing is saved or synced until "Rename all", which
/// renames every changed one as one action (<see cref="TaskActionsService.RenameEachAsync"/>, one undo step); leaving partway changes
/// nothing. Holds the user's titles: shown only.
/// </summary>
public sealed partial class RenameOneByOneViewModel : ObservableObject
{
    private readonly Func<IReadOnlyDictionary<Guid, string>, Task> _renameAll;
    private readonly IReadOnlyList<(Guid Id, string Title)> _tasks;
    private readonly Dictionary<Guid, string> _names = [];
    private bool _loading;

    /// <param name="renameAll">Renames every changed task as one action (<see cref="TaskActionsService.RenameEachAsync"/>).</param>
    /// <param name="tasks">The tasks in the order they show, with their titles now.</param>
    public RenameOneByOneViewModel(Func<IReadOnlyDictionary<Guid, string>, Task> renameAll, IReadOnlyList<(Guid Id, string Title)> tasks)
    {
        _renameAll = renameAll;
        _tasks = tasks;
        _loading = true;
        OldTitle = NewTitle = string.Empty;
        _loading = false;
        Load();
    }

    /// <summary>Asks the dialog to close.</summary>
    public event EventHandler? CloseRequested;

    public int Count => _tasks.Count;

    public bool IsMany => Count > 1;

    /// <summary>"Rename all" with several, "Rename" with one.</summary>
    public string ApplyText => IsMany ? "Rename all" : "Rename";

    public string Foot => "Enter goes to the next one · Alt+←/→ steps · nothing changes until Rename all";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(IsLast))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    public partial int Index { get; set; }

    /// <summary>"Rename 2 of 5", or "Rename" for one task.</summary>
    public string Heading => IsMany ? $"Rename {Index + 1} of {Count}" : "Rename";

    public bool IsLast => Index >= Count - 1;

    /// <summary>The name it has now (italic, over the box).</summary>
    [ObservableProperty]
    public partial string OldTitle { get; set; }

    [ObservableProperty]
    public partial string NewTitle { get; set; }

    /// <summary>One mark per task: the ones up to this one are filled.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<bool> Progress { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsBusy { get; set; }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Previous() => Index--;

    private bool CanGoBack() => Index > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Next() => Index++;

    private bool CanGoForward() => Index < Count - 1;

    /// <summary>Enter in the box: the next task, or Rename all on the last one.</summary>
    public void Enter()
    {
        if (IsLast)
        {
            ApplyCommand.Execute(null);
        }
        else
        {
            Index++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            Dictionary<Guid, string> changed = _names
                .Where(pair => pair.Value.Trim().Length > 0 && _tasks.First(task => task.Id == pair.Key).Title != pair.Value.Trim())
                .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
            if (changed.Count > 0)
            {
                await _renameAll(changed);
            }
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanApply() => !IsBusy;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnIndexChanged(int value) => Load();

    partial void OnNewTitleChanged(string value)
    {
        if (!_loading && Index >= 0 && Index < Count)
        {
            _names[_tasks[Index].Id] = value;
        }
    }

    private void Load()
    {
        if (Count == 0)
        {
            return;
        }

        (Guid id, string title) = _tasks[Math.Clamp(Index, 0, Count - 1)];
        _loading = true;
        OldTitle = title;
        NewTitle = _names.TryGetValue(id, out string? typed) ? typed : title;
        _loading = false;
        Progress = Enumerable.Range(0, Count).Select(index => index <= Index).ToList();
    }
}
