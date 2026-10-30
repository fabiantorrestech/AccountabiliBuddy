using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A subtask row of the checklist: tick it, pick it for the on-screen bar, or rename, move or remove it while editing.</summary>
public sealed partial class SubtaskRowViewModel : ChecklistRowViewModel, ITaskMenuSource
{
    public SubtaskRowViewModel(SessionSubtask subtask, bool isCurrent, SessionTasksService tasks)
        : base(subtask.Id, subtask.Title, subtask.IsDone, isCurrent, tasks)
    {
        NewAfterTitle = string.Empty;
    }

    /// <summary>"Add a step after": the box under this step is open.</summary>
    [ObservableProperty]
    public partial bool IsAddingAfter { get; set; }

    [ObservableProperty]
    public partial string NewAfterTitle { get; set; }

    /// <summary>The step's ⋯ menu: Rename, Add a step after, Delete. A step has no category or place of its own: it goes with its task.</summary>
    public IReadOnlyList<TaskMenuEntry> BuildMenu()
    {
        return
        [
            TaskMenuEntry.Do("Rename", () => BeginEditCommand.Execute(null), MaterialIconKind.PencilOutline),
            TaskMenuEntry.Do("Add a step after", () =>
            {
                NewAfterTitle = string.Empty;
                IsAddingAfter = true;
            }, MaterialIconKind.PlaylistPlus),
            TaskMenuEntry.Separator,
            TaskMenuEntry.Async("Delete", () => Tasks.RemoveAsync(Id), MaterialIconKind.DeleteOutline),
        ];
    }

    [RelayCommand]
    private async Task SaveAddAfterAsync()
    {
        string title = NewAfterTitle;
        IsAddingAfter = false;
        await Tasks.AddSubtaskAfterAsync(Id, title);
    }

    [RelayCommand]
    private void CancelAddAfter()
    {
        IsAddingAfter = false;
    }

    /// <summary>Its place among its task's open subtasks in the order shown (1, 2, 3…); 0 = not lettered (done).</summary>
    public int Letter { get; set; }

    public bool HasLetter => Letter > 0;

    /// <summary>E.g. "a."</summary>
    public string LetterText => SubtaskLetters.Of(Letter);
}
