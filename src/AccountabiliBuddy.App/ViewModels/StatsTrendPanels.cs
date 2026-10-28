using System.Globalization;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Builds the Focus graph: a daily average of deep work and drift per week over the last 12 weeks (Month), or day by day over
/// the last two weeks (Week). Returns null when there is no deep work in it.
/// </summary>
internal static class StatsTrendPanels
{
    private const int Weeks = 12;
    private const int DailyDays = 14;
    private const double GoalHeadroom = 1.25;
    private static readonly TimeSpan SteadyBand = TimeSpan.FromMinutes(15);

    public static StatsTrendPanel? Trend(StatsData data, StatsLookups lookups, StatsRange range)
    {
        return range == StatsRange.Week ? Daily(data, lookups) : Weekly(data, lookups);
    }

    private static StatsTrendPanel? Weekly(StatsData data, StatsLookups lookups)
    {
        Dictionary<DateOnly, TimeSpan> driftPerDay = DriftPerDay(data);
        FocusTrend trend = FocusTrend.Build(data.HistoryDays, driftPerDay, lookups.IsWorkday, data.Today, data.Goal, Weeks);
        if (!trend.HasData)
        {
            return null;
        }

        List<double?> main = trend.Weeks.Select(week => week.DailyFocus?.TotalMinutes).ToList();
        List<double?> drift = trend.Weeks.Select(week => week.DailyDrift?.TotalMinutes).ToList();
        // The newest week is labelled, and every second one before it.
        List<string?> labels = trend.Weeks
            .Select((week, i) => (string?)((trend.Weeks.Count - 1 - i) % 2 == 0 ? $"W{ISOWeek.GetWeekOfYear(week.Start.ToDateTime(TimeOnly.MinValue))}" : null))
            .ToList();
        List<string> tips = trend.Weeks.Select(week => week.DailyFocus is { } focus
            ? $"Week of {week.Start.ToString("MMM d", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(focus)} a day · drift {StatsFormat.Duration(week.DailyDrift ?? TimeSpan.Zero)} a day"
            : $"Week of {week.Start.ToString("MMM d", CultureInfo.CurrentCulture)} · no data").ToList();

        var cards = new List<StatsCard>();
        if (trend.Latest is { DailyFocus: { } latest })
        {
            string detail = trend.Previous is { DailyFocus: { } before }
                ? (latest - before).Duration() < TimeSpan.FromMinutes(1) ? "same as last week"
                    : latest > before ? $"up {StatsFormat.Duration(latest - before)} on last week" : $"down {StatsFormat.Duration(before - latest)} on last week"
                : "so far this week";
            string title = trend.Latest.Start == StatsFormat.MondayOf(data.Today) ? "This week" : "Last week";
            cards.Add(new StatsCard(title, $"{StatsFormat.HoursMinutes(latest)} / day", detail));
        }

        if (trend.Change is { } change)
        {
            string sign = change < TimeSpan.Zero ? "−" : "+";
            string word = change > SteadyBand ? "steady climb" : change < -SteadyBand ? "slipping" : "holding steady";
            cards.Add(new StatsCard($"{trend.TrendCount}-week trend", $"{sign}{Math.Abs(change.TotalHours).ToString("0.0", CultureInfo.CurrentCulture)}h", word));
        }

        if (data.LoggingOn && trend.DriftAverage is { } average)
        {
            cards.Add(new StatsCard("Drift", $"{StatsFormat.Duration(average)} / day", $"average over {trend.TrendCount} weeks"));
        }

        return Panel(data, main, data.LoggingOn ? drift : null, labels, tips, cards);
    }

    private static StatsTrendPanel? Daily(StatsData data, StatsLookups lookups)
    {
        Dictionary<DateOnly, TimeSpan> driftPerDay = DriftPerDay(data);
        List<DeepWorkDay> days = data.HistoryDays.TakeLast(DailyDays).ToList();
        if (days.All(day => day.Focused <= TimeSpan.Zero))
        {
            return null;
        }

        // Off days with no focus are gaps, and today isn't counted until its goal is met (as in Month and Consistency).
        bool Counts(DeepWorkDay day) => (lookups.IsWorkday(day.Date) || day.Focused > TimeSpan.Zero)
            && !(day.Date == data.Today && lookups.IsWorkday(day.Date) && day.Focused < data.Goal);
        List<double?> main = days.Select(day => Counts(day) ? (double?)day.Focused.TotalMinutes : null).ToList();
        List<double?> drift = days.Select(day => Counts(day) ? (double?)driftPerDay.GetValueOrDefault(day.Date).TotalMinutes : null).ToList();
        List<string?> labels = days.Select(day => (string?)StatsFormat.WeekdayLetter(day.Date)).ToList();
        List<string> tips = days.Select(day => Counts(day)
            ? $"{day.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(day.Focused)}" + (data.LoggingOn ? $" · drift {StatsFormat.Duration(driftPerDay.GetValueOrDefault(day.Date))}" : string.Empty)
            : $"{day.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · not counted").ToList();

        List<DeepWorkDay> counted = days.Where(Counts).ToList();
        if (counted.Count == 0)
        {
            return null;
        }

        TimeSpan average = TimeSpan.FromTicks((long)counted.Average(day => day.Focused.Ticks));
        DeepWorkDay best = counted.MaxBy(day => day.Focused)!;
        var cards = new List<StatsCard>
        {
            new("Average", $"{StatsFormat.HoursMinutes(average)} / day", $"over {counted.Count} counted days"),
            new("Best day", StatsFormat.HoursMinutes(best.Focused), best.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)),
        };
        if (data.LoggingOn && driftPerDay.Count > 0)
        {
            TimeSpan driftAverage = TimeSpan.FromTicks((long)counted.Average(day => driftPerDay.GetValueOrDefault(day.Date).Ticks));
            cards.Add(new StatsCard("Drift", $"{StatsFormat.Duration(driftAverage)} / day", "average"));
        }

        return Panel(data, main, data.LoggingOn ? drift : null, labels, tips, cards);
    }

    private static StatsTrendPanel Panel(
        StatsData data, List<double?> main, List<double?>? drift, List<string?> labels, List<string> tips, List<StatsCard> cards)
    {
        double goal = data.Goal.TotalMinutes;
        double maximum = Math.Max(goal * GoalHeadroom, main.Max() ?? 0);
        return new StatsTrendPanel(main, drift, labels, tips, maximum, goal, $"goal {StatsFormat.Duration(data.Goal)} / day", cards);
    }

    private static Dictionary<DateOnly, TimeSpan> DriftPerDay(StatsData data)
    {
        return data.Drift.GroupBy(visit => data.DateOf(visit.Start))
            .ToDictionary(group => group.Key, group => group.Aggregate(TimeSpan.Zero, (sum, visit) => sum + (visit.End - visit.Start)));
    }
}
