namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day on History's deep-work chart.</summary>
/// <param name="Label">E.g. "Thu", or the day of the month on the 30-day chart.</param>
/// <param name="Fraction">The bar's height, 0 to 1 of the chart's scale.</param>
/// <param name="Met">The day reached the daily goal.</param>
/// <param name="Tip">E.g. "Thu 22 Oct · 1h 20m".</param>
public sealed record DeepWorkBar(string Label, double Fraction, bool Met, bool IsToday, string Tip);
