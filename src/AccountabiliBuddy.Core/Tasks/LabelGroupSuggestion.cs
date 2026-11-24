namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One suggested label group (ADR 93, <see cref="LabelGroupSuggestions"/>): a name prefix two or more uncategorised labels share.
/// </summary>
/// <param name="Name">The group's name: the prefix as the first of its labels (by position) writes it.</param>
/// <param name="ExistingGroupId">A live group that already has this name (the labels go into it instead of a new one); null when none.</param>
/// <param name="Labels">The labels that would go in, in the labels' order.</param>
public sealed record LabelGroupSuggestion(string Name, Guid? ExistingGroupId, IReadOnlyList<LabelGroupSuggestionItem> Labels);
