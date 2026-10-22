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
}
