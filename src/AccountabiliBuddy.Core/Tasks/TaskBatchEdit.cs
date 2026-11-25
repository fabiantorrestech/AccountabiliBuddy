using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What Move and label's Apply changes on the tasks being edited (Tasks redesign 8c), all at once as one undo step
/// (<see cref="TaskActions.EditMany"/>): a bin, a label (or none), tags put on and tags taken off. Nothing picked for a part keeps it as it is.
/// </summary>
/// <param name="Bin">The bin to move them to; null keeps each task's bin. This session and Finished today are not offered (nothing happens).</param>
/// <param name="SetsLabel">A label pick was made: <paramref name="LabelId"/> is the new label, null for No label.</param>
/// <param name="LabelId">The label to give them (with <paramref name="SetsLabel"/>); null = No label.</param>
/// <param name="AddTags">Tags to put on every task.</param>
/// <param name="RemoveTags">Tags to take off every task.</param>
public sealed record TaskBatchEdit(TaskBin? Bin, bool SetsLabel, Guid? LabelId, IReadOnlyList<string> AddTags, IReadOnlyList<string> RemoveTags)
{
    /// <summary>Nothing is picked: Apply has nothing to do.</summary>
    public bool IsEmpty => Bin is null && !SetsLabel && AddTags.Count == 0 && RemoveTags.Count == 0;
}
