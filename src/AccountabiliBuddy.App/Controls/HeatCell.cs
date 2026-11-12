namespace AccountabiliBuddy.App.Controls;

/// <summary>One cell of a heatmap.</summary>
/// <param name="Level">-1 for nothing to show (not drawn: a day that hasn't happened, or isn't kept), 0 for none, up to 4 for the most.</param>
/// <param name="Tip">Shown on hover, e.g. "Tue, Oct 27 · 2h 05m".</param>
/// <param name="Date">The day it stands for, which a click selects; null when a click does nothing.</param>
/// <param name="Mark">How the cell is drawn beyond its level (round 2 goal heatmap, brief §3.8); Stats leaves it <see cref="HeatCellMark.None"/>.</param>
/// <param name="Progress">For <see cref="HeatCellMark.Today"/>: how far today is, 0 to 1 (the cell fills from the bottom).</param>
public readonly record struct HeatCell(int Row, int Column, int Level, string Tip, DateOnly? Date = null, HeatCellMark Mark = HeatCellMark.None, double Progress = 0);
