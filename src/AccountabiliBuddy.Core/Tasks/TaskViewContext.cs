using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a view needs besides its settings to pick and order tasks (<see cref="TaskViewRules"/>).</summary>
/// <param name="Now">The trusted time: "untouched" and overdue count from it.</param>
/// <param name="Zone">The zone that says which day it is (due dates are local dates).</param>
/// <param name="Labels">The labels, for grouping by label (their order and names).</param>
/// <param name="Search">Search words (a number like "142b", or text); only matching tasks stay. Null or blank for none.</param>
/// <param name="Scope">The bins the view is about (the Shelf's three for the Shelf); null for every bin. Tasks elsewhere never show.</param>
/// <param name="ShowEmptyBins">Group by bins lists every bin of the scope, empty ones too.</param>
/// <param name="LabelShown">ADR 89: whether this device shows a label. A task whose label it doesn't show reads as No label (grouping, the No label filter, search). Null: every label.</param>
/// <param name="TagShown">ADR 89: whether this device shows a tag; tags it doesn't show make no group, no search match, and don't count as a tag. Null: every tag.</param>
/// <param name="Groups">ADR 93: the label groups, for ordering Group by Labels (group position, then label position; uncategorised labels last). Null: none.</param>
public sealed record TaskViewContext(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    IReadOnlyList<TaskLabel> Labels,
    string? Search = null,
    IReadOnlyList<TaskBin>? Scope = null,
    bool ShowEmptyBins = false,
    Func<TaskLabel, bool>? LabelShown = null,
    Func<string, bool>? TagShown = null,
    IReadOnlyList<TaskLabelGroup>? Groups = null)
{
    // Worked out once per query, not once per task (a view of hundreds of tasks asks for them for each).
    private IReadOnlyList<TaskLabel>? _shownLabels;
    private HashSet<Guid>? _shownLabelIds;

    /// <summary>The labels this device shows.</summary>
    public IReadOnlyList<TaskLabel> ShownLabels => _shownLabels ??= LabelShown is null ? Labels : Labels.Where(LabelShown).ToList();

    /// <summary>The ids of the live labels this device shows.</summary>
    public HashSet<Guid> ShownLabelIds => _shownLabelIds ??= ShownLabels.Where(label => label.IsLive).Select(label => label.Id).ToHashSet();

    /// <summary>The task's tags this device shows (null: none).</summary>
    public IReadOnlyList<string>? ShownTags(TaskItem task)
    {
        return TagShown is null || task.Tags is null ? task.Tags : TaskLabels.Clean(task.Tags.Where(TagShown));
    }
}
