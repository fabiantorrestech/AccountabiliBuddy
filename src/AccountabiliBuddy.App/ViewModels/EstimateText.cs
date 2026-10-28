namespace AccountabiliBuddy.App.ViewModels;

/// <summary>How a task's estimate reads (ADR 50): "50 min", "90 min", "2h 15m".</summary>
public static class EstimateText
{
    /// <summary>E.g. 25 -> "25 min", 90 -> "90 min", 135 -> "2h 15m", 120 -> "2h".</summary>
    public static string Of(int minutes)
    {
        if (minutes < 100)
        {
            return $"{minutes} min";
        }

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest:00}m";
    }

    /// <summary>The estimates of the tasks added up, e.g. "1h 30m"; empty when none has one.</summary>
    public static string Total(IEnumerable<int?> estimates)
    {
        int total = estimates.Sum(minutes => minutes ?? 0);
        return total > 0 ? Of(total) : string.Empty;
    }
}
