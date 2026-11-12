using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The live step list of a running session's task (P3; opened from ▾ on a step's "↳ task · ● label · 142b" line): its steps with their
/// letters, a tick strikes one through, the target brings an open one into focus (the session's cap decides: full opens "This session is
/// full"), and the "+ Add a step" pill with ». It stays open and reads the task again after each change. Titles are shown only.
/// </summary>
public sealed partial class StepListViewModel : ObservableObject
{
    private readonly Func<SessionTask?> _read;
    private readonly Func<IReadOnlyCollection<Guid>> _focused;
    private readonly Func<Guid, Task> _toggle;
    private readonly Func<Guid, Task> _focus;
    private readonly Func<string, Task> _add;

    /// <param name="read">The task as it is now in the running checklist (null once it left it).</param>
    /// <param name="focused">The running session's steps in focus.</param>
    public StepListViewModel(
        Func<SessionTask?> read, Func<IReadOnlyCollection<Guid>> focused, Func<Guid, Task> toggle, Func<Guid, Task> focus, Func<string, Task> add)
    {
        _read = read;
        _focused = focused;
        _toggle = toggle;
        _focus = focus;
        _add = add;
        NewStep = string.Empty;
        Title = string.Empty;
        DoneText = string.Empty;
        Reload();
    }

    /// <summary>The task's title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>"1/4 done".</summary>
    [ObservableProperty]
    public partial string DoneText { get; set; }

    public ObservableCollection<StepListItemViewModel> Items { get; } = new();

    /// <summary>The add pill's text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewStep))]
    [NotifyCanExecuteChangedFor(nameof(AddStepCommand))]
    public partial string NewStep { get; set; }

    /// <summary>The » button shows once something is typed.</summary>
    public bool HasNewStep => NewStep.Trim().Length > 0;

    /// <summary>Reads the task again (after a change here or elsewhere).</summary>
    public void Reload()
    {
        SessionTask? task = _read();
        Items.Clear();
        if (task is null)
        {
            Title = "This task left the session";
            DoneText = string.Empty;
            return;
        }

        Title = task.Title;
        DoneText = $"{task.Subtasks.Count(step => step.IsDone)}/{task.Subtasks.Count} done";
        IReadOnlyCollection<Guid> focused = _focused();
        for (int index = 0; index < task.Subtasks.Count; index++)
        {
            SessionSubtask step = task.Subtasks[index];
            Guid id = step.Id;
            bool isFocused = !step.IsDone && focused.Contains(id);
            Items.Add(new StepListItemViewModel(
                TaskStepLetters.Of(index) + ".", step.Title, step.IsDone, isFocused,
                new AsyncRelayCommand(() => RunAsync(() => _toggle(id))),
                step.IsDone || isFocused ? null : new AsyncRelayCommand(() => RunAsync(() => _focus(id)))));
        }
    }

    [RelayCommand(CanExecute = nameof(HasNewStep))]
    private async Task AddStepAsync()
    {
        string title = NewStep.Trim();
        if (title.Length == 0)
        {
            return;
        }

        NewStep = string.Empty;
        await RunAsync(() => _add(title));
    }

    private async Task RunAsync(Func<Task> action)
    {
        await action();
        Reload();
    }
}
