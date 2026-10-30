namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the Tasks page's filter button remembers on this PC (the document "tasks.view"): names of the enum values, never synced.
/// </summary>
/// <param name="Statuses">The <c>TaskStatusKind</c> names that are shown; empty means all of them.</param>
/// <param name="CategoryIds">The categories shown; empty (with no "No category") means every category.</param>
/// <param name="NoCategory">Rows without a category are shown too.</param>
/// <param name="Due">A <c>TaskDueFilter</c> name.</param>
/// <param name="Sort">A <c>TaskSortMode</c> name.</param>
public sealed record TaskViewPreferences(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<Guid> CategoryIds,
    bool NoCategory,
    string Due,
    string Sort);
