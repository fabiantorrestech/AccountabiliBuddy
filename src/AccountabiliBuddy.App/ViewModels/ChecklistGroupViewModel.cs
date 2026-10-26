using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The open tasks of one category in the checklist, under a header in the category's colour; the tasks with no category
/// come last, under "No category".
/// </summary>
/// <param name="ShowsHeader">False while no open task has a category, so a list without categories looks as it always did.</param>
public sealed record ChecklistGroupViewModel(string Name, IBrush Brush, bool HasCategory, bool ShowsHeader, IReadOnlyList<TaskRowViewModel> Tasks);
