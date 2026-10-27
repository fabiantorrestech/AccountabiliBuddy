using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A subtask row of the checklist: tick it, pick it for the on-screen bar, or rename, move or remove it while editing.</summary>
public sealed class SubtaskRowViewModel : ChecklistRowViewModel
{
    public SubtaskRowViewModel(SessionSubtask subtask, bool isCurrent, SessionTasksService tasks)
        : base(subtask.Id, subtask.Title, subtask.IsDone, isCurrent, tasks)
    {
    }

    /// <summary>Its place among its task's open subtasks in the order shown (1, 2, 3…); 0 = not lettered (done).</summary>
    public int Letter { get; set; }

    public bool HasLetter => Letter > 0;

    /// <summary>E.g. "a."</summary>
    public string LetterText => SubtaskLetters.Of(Letter);
}
