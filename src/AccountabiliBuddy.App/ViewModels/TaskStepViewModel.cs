using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One step of the task shown in the Tasks page's detail card: a check box, its title, and a way to take it off.</summary>
public sealed partial class TaskStepViewModel
{
    private readonly Func<Guid, Task> _toggle;
    private readonly Func<Guid, Task> _remove;

    public TaskStepViewModel(Guid id, string title, bool isDone, bool canEdit, Func<Guid, Task> toggle, Func<Guid, Task> remove)
    {
        Id = id;
        Title = title;
        IsDone = isDone;
        CanEdit = canEdit;
        _toggle = toggle;
        _remove = remove;
        ToggleCommand = new AsyncRelayCommand(() => _toggle(Id));
        RemoveCommand = new AsyncRelayCommand(() => _remove(Id));
    }

    public Guid Id { get; }

    public string Title { get; }

    public bool IsDone { get; }

    /// <summary>False for a task that is already done: its steps are shown, not changed.</summary>
    public bool CanEdit { get; }

    /// <summary>Ticks or unticks the step (the check box).</summary>
    public ICommand ToggleCommand { get; }

    public ICommand RemoveCommand { get; }
}
