using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A task the add box (or Jot it down) asks for: the title without its "@label" and "#tag" words, where it goes (This session goes
/// through the session's cap), the label and tags picked or typed, and the note (the tail "Move … to notes" took off the title).
/// </summary>
public sealed record AddBoxRequest(string Title, TaskBin Destination, Guid? LabelId, IReadOnlyList<string> Tags, string? Note);
