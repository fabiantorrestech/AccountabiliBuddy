using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The tasks of one category in a list of the Tasks page, under a header with the category's colour dot and name (Muted 12,
/// SemiBold) and set in about 16; "No category" comes last and has no header when it is the only group.
/// </summary>
/// <param name="Brush">The category's colour (its dot).</param>
/// <param name="HasCategory">False for "No category" (no dot).</param>
/// <param name="ShowsHeader">False when the list has no category at all, so it reads as it always did.</param>
public sealed record TaskGroupViewModel(
    string Name,
    IBrush Brush,
    bool HasCategory,
    bool ShowsHeader,
    IReadOnlyList<TaskListRowViewModel> Rows);
