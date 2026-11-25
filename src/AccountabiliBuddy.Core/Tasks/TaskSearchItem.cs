using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What Search filters look at in one task, wherever it comes from (a task of the Shelf, an Archive row): its text, label, tags, bin
/// and the time "When" reads. Holds the user's task text: never logged.
/// </summary>
/// <param name="Steps">Its steps' titles.</param>
/// <param name="LabelId">Its label; null for none.</param>
/// <param name="Bin">The Bins chip it falls under; null where Bins don't apply (the Archive).</param>
/// <param name="When">The time "When" filters by: the created date on the Shelf, the finished or deleted time in the Archive; null when unknown.</param>
public sealed record TaskSearchItem(
    string Title,
    IReadOnlyList<string> Steps,
    string? Note,
    IReadOnlyList<string> Tags,
    Guid? LabelId,
    TaskSearchBin? Bin,
    DateTimeOffset? When)
{
    /// <summary>A task of the task list as the Shelf searches it: "When" is its created date.</summary>
    public static TaskSearchItem Of(TaskItem task)
    {
        return new TaskSearchItem(
            task.Title, (task.Steps ?? []).Select(step => step.Title).ToList(), task.Note, task.Tags ?? [], task.LabelId,
            TaskSearchFilters.BinOf(task.Bin), task.CreatedAt);
    }
}
