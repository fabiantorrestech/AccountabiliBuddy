namespace AccountabiliBuddy.App.Controls;

/// <summary>One bar on the Stats page's bar chart.</summary>
/// <param name="Label">What sits under the bar: a weekday letter, a day of the month, an hour.</param>
/// <param name="Fraction">The bar's height, 0 to 1 of the chart's scale.</param>
/// <param name="IsToday">Its label is drawn in bold.</param>
/// <param name="IsFuture">Hasn't happened yet: no bar, a faint label (never a zero).</param>
/// <param name="Tip">Shown on hover, e.g. "Tue, Oct 27 · 1h 20m".</param>
/// <param name="Date">The day it stands for, which a click on the bar selects; null when a click does nothing.</param>
public sealed record StatsBar(
    string Label, double Fraction, StatsBarKind Kind, bool IsToday, bool IsFuture, string Tip, DateOnly? Date = null);
