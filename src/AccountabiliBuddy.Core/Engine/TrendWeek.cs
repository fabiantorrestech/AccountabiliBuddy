namespace AccountabiliBuddy.Core.Engine;

/// <summary>One week (Monday to Sunday) on the Stats page's Focus graph.</summary>
/// <param name="Start">The week's Monday.</param>
/// <param name="DailyFocus">Deep work per day over the week's workdays so far (and any other day with focus); null when no day counts.</param>
/// <param name="DailyDrift">Drift per day over the same days; null when no day counts.</param>
public sealed record TrendWeek(DateOnly Start, TimeSpan? DailyFocus, TimeSpan? DailyDrift);
