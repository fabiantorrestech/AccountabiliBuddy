namespace AccountabiliBuddy.Core.Tasks;

/// <summary>One label of a <see cref="LabelGroupSuggestion"/>, with the shorter name "Shorten the names" would give it.</summary>
/// <param name="LabelId">The label.</param>
/// <param name="Name">Its name now ("Work - AI").</param>
/// <param name="ShortName">The part after the prefix ("AI").</param>
/// <param name="Skipped">The short name would clash with another live label or another short name: it is never renamed (it still goes in the group).</param>
/// <param name="SkipReason">Why it is skipped, for the page; null when it isn't.</param>
public sealed record LabelGroupSuggestionItem(Guid LabelId, string Name, string ShortName, bool Skipped, string? SkipReason);
