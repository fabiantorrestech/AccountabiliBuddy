namespace AccountabiliBuddy.App.Controls;

/// <summary>One cell of a heatmap.</summary>
/// <param name="Level">-1 for nothing to show (not drawn: a day that hasn't happened, or isn't kept), 0 for none, up to 4 for the most.</param>
/// <param name="Tip">Shown on hover, e.g. "Tue, Oct 27 · 2h 05m".</param>
/// <param name="Date">The day it stands for, which a click selects; null when a click does nothing.</param>
public readonly record struct HeatCell(int Row, int Column, int Level, string Tip, DateOnly? Date = null);
