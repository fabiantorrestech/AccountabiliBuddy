using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Steps view: the days a paired phone read from Health Connect (the phone's sync file carries up to 14), as bars with the goal line.
/// A day the file has no count for is a bar-less day ("no data"), never a zero, so a period longer than the file shows only what it has.
/// </summary>
internal static class StatsStepsPanels
{
    private const double GoalHeadroom = 1.15;

    /// <returns>Null when no phone shares steps, or none of the file's days falls in <paramref name="period"/>.</returns>
    public static StatsBarPanel? Build(SyncSteps? steps, StatsPeriod period, DateOnly today)
    {
        if (steps is null)
        {
            return null;
        }

        Dictionary<DateOnly, int> perDay = steps.Days.Where(day => day.Date >= period.From && day.Date <= period.To && day.Date <= today)
            .ToDictionary(day => day.Date, day => day.Count);
        if (perDay.Count == 0)
        {
            return null;
        }

        double scale = Math.Max(steps.Goal * GoalHeadroom, perDay.Values.Max());
        var bars = new List<StatsBar>();
        for (DateOnly date = period.From; date <= period.To; date = date.AddDays(1))
        {
            bool has = perDay.TryGetValue(date, out int count);
            bool noBar = date > today || !has;
            StatsBarKind kind = !has ? StatsBarKind.Plan : count >= steps.Goal ? StatsBarKind.Met : date == today ? StatsBarKind.Accent : StatsBarKind.Plan;
            string day = date.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
            string description = !has ? $"{day} · no data" : $"{day} · {count:N0} steps" + (count >= steps.Goal ? " · goal met" : string.Empty);
            bars.Add(new StatsBar(
                period.Days <= 7 ? StatsFormat.WeekdayLetter(date) : date.Day.ToString(CultureInfo.CurrentCulture),
                has ? count / scale : 0, kind, date == today, noBar, description, null));
        }

        int average = (int)Math.Round(perDay.Values.Average(), MidpointRounding.ToEven);
        int met = perDay.Values.Count(count => count >= steps.Goal);
        var cards = new List<StatsCard>
        {
            new("Goal met", $"{met} of {perDay.Count}", perDay.Count == 1 ? "day with steps counted" : "days with steps counted"),
            new("Best day", perDay.Values.Max().ToString("N0", CultureInfo.CurrentCulture), "steps"),
        };
        return new StatsBarPanel(
            average.ToString("N0", CultureInfo.CurrentCulture),
            $"steps a day on average · {perDay.Count} {(perDay.Count == 1 ? "day" : "days")} from your phone's Health Connect",
            bars, steps.Goal / scale, $"goal {steps.Goal:N0}", cards);
    }
}
