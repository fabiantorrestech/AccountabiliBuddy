namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What the add box makes of typed text: the title without its "@label" and "#tag" words, the label and the tags (private text).</summary>
public sealed record TaskTypedTitle(string Title, Guid? LabelId, IReadOnlyList<string> Tags);
