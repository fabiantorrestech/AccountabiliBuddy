using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The open tasks of one category in the checklist, under a header in the category's colour; the tasks with no category
/// come last, under "No category".
/// </summary>
/// <param name="Brush">The category's colour (its dot).</param>
/// <param name="NameBrush">
/// The colour of a category's name: the category's own, or white on the on-screen window's dark panel (the dot keeps the colour).
/// </param>
/// <param name="ShowsHeader">False while no open task has a category, so a list without categories looks as it always did.</param>
public sealed record ChecklistGroupViewModel(
    string Name,
    IBrush Brush,
    IBrush NameBrush,
    bool HasCategory,
    bool ShowsHeader,
    IReadOnlyList<TaskRowViewModel> Tasks);
