namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// One day's value from its datapoints by the Beeminder goal's own day rule (<c>aggday</c>, ADR 84): sum, last, first, min, max,
/// count, mean (also "truemean"), "uniqmean" and median. Anything else, or nothing, is a sum.
/// </summary>
public static class BeeminderDayValue
{
    public static double Of(string? aggDay, IReadOnlyList<BeeminderDatapoint> dayPoints)
    {
        if (dayPoints.Count == 0)
        {
            return 0;
        }

        IOrderedEnumerable<BeeminderDatapoint> inOrder = dayPoints.OrderBy(point => point.Order).ThenBy(point => point.Id, StringComparer.Ordinal);
        return (aggDay ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "last" => inOrder.Last().Value,
            "first" => inOrder.First().Value,
            "min" => dayPoints.Min(point => point.Value),
            "max" => dayPoints.Max(point => point.Value),
            "count" => dayPoints.Count,
            "mean" or "truemean" => dayPoints.Average(point => point.Value),
            "uniqmean" => dayPoints.Select(point => point.Value).Distinct().Average(),
            "median" => Median(dayPoints.Select(point => point.Value).Order().ToList()),
            _ => dayPoints.Sum(point => point.Value),
        };
    }

    private static double Median(List<double> sorted)
    {
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
