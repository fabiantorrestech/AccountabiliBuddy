namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// One day's value from its datapoints by the Beeminder goal's own day rule (<c>aggday</c>, ADR 84): sum, last, first, min, max,
/// count, mean (the mean of the UNIQUE values), "truemean" (the plain mean), "uniqmean", median, binary, nonzero and cap1. Anything
/// else, or nothing, is a sum.
/// <para>
/// These follow Beeminder's beebrain rules as the author knows them, not as verified from the API docs (reasoning only): "mean" =
/// mean of the distinct values, "truemean" = mean of all values, "uniqmean" = mean of the distinct values, "binary" = 1 when the day
/// has any datapoint else 0, "nonzero" = 1 when any value is not 0, "cap1" = min(sum, 1). Check them against Beeminder's aggday
/// list before trusting a goal that uses one of the rarer rules.
/// </para>
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
            "truemean" => dayPoints.Average(point => point.Value),
            "mean" or "uniqmean" => dayPoints.Select(point => point.Value).Distinct().Average(),
            "binary" => 1,
            "nonzero" => dayPoints.Any(point => point.Value != 0) ? 1 : 0,
            "cap1" => Math.Min(dayPoints.Sum(point => point.Value), 1),
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
