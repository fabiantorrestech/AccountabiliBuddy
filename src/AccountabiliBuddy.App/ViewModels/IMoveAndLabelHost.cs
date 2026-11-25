using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What Move and label needs from the Tasks page (<see cref="TasksViewModel"/>): the tasks as stored, the labels and tags this device shows, the label picker, and Apply.</summary>
internal interface IMoveAndLabelHost
{
    /// <summary>The live task in the task list; null when it is gone.</summary>
    TaskItem? TaskOf(Guid id);

    /// <summary>The task's label as this device shows it; null for No label (a label hidden here reads as No label, ADR 89).</summary>
    MoveAndLabelLabel? ShownLabelOf(TaskItem task);

    /// <summary>A live label this device shows; null otherwise.</summary>
    MoveAndLabelLabel? LabelById(Guid labelId);

    /// <summary>The tags of a task this device shows.</summary>
    IReadOnlyList<string> ShownTags(IReadOnlyList<string>? tags);

    /// <summary>Every tag this device shows, in name order.</summary>
    IReadOnlyList<string> KnownTags();

    /// <summary>The shared label picker over the labels this device shows, tagged for <paramref name="currentLabels"/>, holding <paramref name="pick"/>.</summary>
    LabelPickerViewModel NewLabelPicker(IReadOnlyList<Guid?> currentLabels, LabelPick pick);

    /// <summary>Apply: one undo step with a toast; its undo entry, or null when nothing changed.</summary>
    Task<TaskUndoEntry?> EditManyAsync(IReadOnlyList<Guid> ids, TaskBatchEdit edit, string toast);

    /// <summary>Done, × or Esc: the dialog closes and the selection clears.</summary>
    void CloseMoveAndLabel();
}
