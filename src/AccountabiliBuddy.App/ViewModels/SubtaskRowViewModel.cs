using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A subtask row of the checklist: tick it, or remove it while editing.</summary>
public sealed partial class SubtaskRowViewModel : ObservableObject
{
    private readonly Func<Guid, Task> _toggle;
    private readonly Func<Guid, Task> _remove;

    public SubtaskRowViewModel(SessionSubtask subtask, Func<Guid, Task> toggle, Func<Guid, Task> remove)
    {
        Id = subtask.Id;
        Title = subtask.Title;
        _isDone = subtask.IsDone;
        _toggle = toggle;
        _remove = remove;
    }

    public Guid Id { get; }

    public string Title { get; }

    private bool _isDone;

    /// <summary>Ticking it here ticks it in the checklist.</summary>
    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (value != _isDone)
            {
                _isDone = value;
                _ = _toggle(Id);
            }
        }
    }

    [RelayCommand]
    private Task RemoveAsync()
    {
        return _remove(Id);
    }
}
