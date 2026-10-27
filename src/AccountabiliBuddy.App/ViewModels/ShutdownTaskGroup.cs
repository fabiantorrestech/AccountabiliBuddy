using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The tasks left open today in one category, on the shutdown card; the ones with no category come last.</summary>
/// <param name="Brush">The category's colour (its dot).</param>
public sealed record ShutdownTaskGroup(string Name, IBrush Brush, bool HasCategory, IReadOnlyList<ShutdownTaskRow> Rows);
