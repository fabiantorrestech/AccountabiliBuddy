namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A task row dropped by its handle (or moved with Alt+↑/↓): from which section to which, and next to which row there.
/// </summary>
/// <param name="From">The section it was dragged from.</param>
/// <param name="To">The section it was dropped in.</param>
/// <param name="Target">The row it was dropped next to; null for the end of the section.</param>
/// <param name="After">It goes after <paramref name="Target"/> (else before it).</param>
/// <param name="NextId">The row after <paramref name="Target"/> in that section, for a place "before the next one"; null at the end.</param>
public sealed record TaskRowDrop(TaskListRowViewModel Row, TaskSectionViewModel From, TaskSectionViewModel To, TaskListRowViewModel? Target, bool After, Guid? NextId);
