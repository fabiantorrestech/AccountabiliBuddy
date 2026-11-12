namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One continuous staircase line between two months of a day heatmap (change note §4a): from the top, down the right side of
/// <see cref="Column"/> to <see cref="Row"/>, across it, then down its left side to the bottom (a straight line when Row is 0).
/// </summary>
/// <param name="Column">The column holding the new month's first day.</param>
/// <param name="Row">That day's row.</param>
public readonly record struct HeatmapDivider(int Column, int Row);
