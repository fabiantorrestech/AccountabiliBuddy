using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>One section of a view's result: a bin, a label (or "No label"), a tag (or "No tag"), or the whole list (group by none).</summary>
/// <param name="Key">A stable key for the section ("bin:sort", "label:{id}", "label:none", "tag:home", "tag:none", "all").</param>
/// <param name="Title">What the section header shows ("To sort", "Work", "#home", "No label").</param>
public sealed record TaskViewGroup(string Key, string Title, TaskBin? Bin, Guid? LabelId, string? Tag, IReadOnlyList<TaskItem> Tasks);
