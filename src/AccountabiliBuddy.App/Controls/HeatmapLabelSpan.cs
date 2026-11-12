namespace AccountabiliBuddy.App.Controls;

/// <summary>A label centred over a run of heatmap columns (a month over its weeks); clipped to the grid, dropped when it would overlap.</summary>
/// <param name="Centre">The run's middle, in columns from the grid's left edge (e.g. 2.5 for columns 1 to 3).</param>
public readonly record struct HeatmapLabelSpan(string Label, double Centre);
