namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One task in a tidy-up: the task as it was when the tidy-up began (<paramref name="Original"/>, so changing a decision re-applies
/// from it), what is suggested for it and what the user chose (null while undecided). Holds the user's private text: memory only.
/// </summary>
public sealed record TidyUpItem(Guid TaskId, TaskUndoEntry Original, TidyUpChoice Suggested, TidyUpChoice? Chosen = null);
