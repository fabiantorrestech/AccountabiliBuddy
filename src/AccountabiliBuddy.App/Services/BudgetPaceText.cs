using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>The Budget day's pace line (ADR 47), e.g. "2h 10m left · 5h of day left", for the goal card and the on-screen overlay.</summary>
public static class BudgetPaceText
{
    /// <summary>"Budget met" once nothing is left, otherwise the budget left and the day left for it.</summary>
    public static string Describe(BudgetPace pace)
    {
        return pace.BudgetLeft <= TimeSpan.Zero
            ? "Budget met"
            : $"{Format(pace.BudgetLeft)} left · {Format(pace.AvailableLeft)} of day left";
    }

    /// <summary>"2h 10m", "5h", "45m", "0m"; whole minutes, rounded up so a nearly-met budget doesn't read as met.</summary>
    public static string Format(TimeSpan span)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, span.TotalMinutes));
        return minutes < 60 ? $"{minutes}m" : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }
}
