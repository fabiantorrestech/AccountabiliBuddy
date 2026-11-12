using System.Globalization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The goals' words and numbers on the PC screens (brief §3.11, change note §4a): 12-hour times, "Wed 11/11" dates (the year only
/// when it differs), whole minutes under an hour then "1h 25m", values in the goal's unit, a goal's target line ("15 min a day",
/// "3× a week", "175 lb by Sun 3/1/2027") and its kind ("daily habit"). Goal names, notes and reasons are never put in a log.
/// </summary>
public static class GoalTexts
{
    /// <summary>"Wed 11/11", or "Sun 1/31/2027" when the year differs from <paramref name="today"/>'s (§3.11).</summary>
    public static string Date(DateOnly day, DateOnly today)
    {
        string weekday = day.ToString("ddd", CultureInfo.InvariantCulture);
        return day.Year == today.Year ? $"{weekday} {day.Month}/{day.Day}" : $"{weekday} {day.Month}/{day.Day}/{day.Year}";
    }

    /// <summary>"2:14 PM" in <paramref name="zone"/> (always 12-hour).</summary>
    public static string Time(DateTimeOffset at, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(at, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>"12 min", "2h", "1h 10m" (whole minutes, rounded down).</summary>
    public static string Minutes(double minutes)
    {
        int whole = (int)Math.Floor(Math.Max(0, minutes) + 1e-9);
        return GoalWhatItTakes.MinutesText(whole);
    }

    /// <summary>A value in the goal's unit: "1h 10m", "6,240", "182.4 lb", "4".</summary>
    public static string Value(double value, GoalUnit unit)
    {
        return unit switch
        {
            GoalUnit.Minutes => Minutes(value),
            GoalUnit.Lb => $"{Number(value, 1)} lb",
            GoalUnit.Kg => $"{Number(value, 1)} kg",
            GoalUnit.Steps => Number(value, 0),
            GoalUnit.YesNo => value > 0 ? "Done" : "Not yet",
            _ => Number(value, 1),
        };
    }

    /// <summary>A target in the goal's unit, short where it stands after a "/" ("2h", "8k", "15 min", "7").</summary>
    public static string ShortValue(double value, GoalUnit unit)
    {
        if (unit == GoalUnit.Steps && value >= 1000)
        {
            return $"{Number(value / 1000, 1)}k";
        }

        return Value(value, unit);
    }

    /// <summary>A whole or one-decimal number with group separators ("6,240", "182.4").</summary>
    public static string Number(double value, int decimals)
    {
        double rounded = Math.Round(value, decimals);
        string format = decimals == 0 || rounded == Math.Floor(rounded) ? "#,0" : "#,0.#";
        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The goal's target in a few words, as the list's "↳" line and the detail's subtitle start: "15 min a day", "2h a day", "3× a
    /// week", "8,000 a day", "175 lb by Sun 3/1/2027", "50h by Sun 1/31"; "tracking only" when nothing is asked.
    /// </summary>
    public static string TargetLine(Goal goal, DateOnly today)
    {
        GoalTarget target = goal.Target;
        string? period = PeriodPart(goal);
        string? longTerm = target.TargetAmount is double amount
            ? (target.TargetDate is DateOnly date ? $"{Value(amount, target.Unit)} by {Date(date, today)}" : $"Reach {Value(amount, target.Unit)}")
            : null;
        if (period is not null && longTerm is not null)
        {
            return goal.Kind == GoalKind.Amount ? longTerm : period;
        }

        return period ?? longTerm ?? "tracking only";
    }

    /// <summary>"daily habit", "weekly habit", "time goal", "amount", "milestone", "built in".</summary>
    public static string KindText(Goal goal)
    {
        if (goal.IsDeepWork)
        {
            return "built in";
        }

        return goal.Kind switch
        {
            GoalKind.Habit => goal.Target.Period switch
            {
                GoalPeriod.Day => "daily habit",
                GoalPeriod.Week => "weekly habit",
                GoalPeriod.TwoWeeks => "two-week habit",
                _ => "monthly habit",
            },
            GoalKind.Time => "time goal",
            GoalKind.Amount => "amount",
            _ => "milestone",
        };
    }

    /// <summary>"a day", "a week", "every 2 weeks", "a month".</summary>
    public static string Per(GoalPeriod period)
    {
        return period switch
        {
            GoalPeriod.Day => "a day",
            GoalPeriod.Week => "a week",
            GoalPeriod.TwoWeeks => "every 2 weeks",
            _ => "a month",
        };
    }

    /// <summary>"Days this week", "Times this week", "This month"... the This period tile's caption.</summary>
    public static string PeriodName(GoalPeriod period)
    {
        return period switch
        {
            GoalPeriod.Day or GoalPeriod.Week => "this week",
            GoalPeriod.TwoWeeks => "these two weeks",
            _ => "this month",
        };
    }

    /// <summary>"1 day", "3 days".</summary>
    public static string Plural(int count, string word)
    {
        return count == 1 ? $"1 {word}" : $"{count} {word}s";
    }

    private static string? PeriodPart(Goal goal)
    {
        GoalTarget target = goal.Target;
        if (goal.Kind == GoalKind.Habit)
        {
            double? threshold = Threshold(goal);
            int? times = target.WeeklyDays ?? (target.Period == GoalPeriod.Day ? null : (int?)Math.Max(1, Math.Round(target.Amount ?? 1)));
            if (target.Period == GoalPeriod.Day)
            {
                return threshold is double minutes ? $"{Minutes(minutes)} a day" : "every day";
            }

            return $"{times}× {Per(target.Period)}";
        }

        if (target.Amount is not double amount)
        {
            return null;
        }

        if (target.WeeklyDays is int days)
        {
            return $"{Value(amount, target.Unit)} on {days} days {Per(target.Period)}";
        }

        string prefix = target.Direction == GoalDirection.AtMost ? "at most " : string.Empty;
        return $"{prefix}{Value(amount, target.Unit)} {Per(target.Period)}";
    }

    /// <summary>A habit's "ticks itself at" minutes (the smallest threshold of its switched-on time sources), or null.</summary>
    public static double? Threshold(Goal goal)
    {
        return goal.Sources
            .Where(rule => rule.Automatic && rule.CountsTime && rule.ThresholdMinutes is > 0)
            .Select(rule => rule.ThresholdMinutes)
            .Min();
    }
}
