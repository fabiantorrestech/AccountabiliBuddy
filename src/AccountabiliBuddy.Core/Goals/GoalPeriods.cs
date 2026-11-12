namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Period maths (brief §5.5): weeks start on the user's week start; two-week periods are counted in pairs from the week the
/// goal was created in; months are calendar months.
/// </summary>
public static class GoalPeriods
{
    public static DayOfWeek FirstDay(GoalWeekStart weekStart)
    {
        return weekStart switch
        {
            GoalWeekStart.Monday => DayOfWeek.Monday,
            GoalWeekStart.Saturday => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday,
        };
    }

    /// <summary>The first day of the week <paramref name="day"/> is in.</summary>
    public static DateOnly WeekStartOf(DateOnly day, GoalWeekStart weekStart)
    {
        int offset = ((int)day.DayOfWeek - (int)FirstDay(weekStart) + 7) % 7;
        return day.AddDays(-offset);
    }

    /// <summary>The week <paramref name="day"/> is in.</summary>
    public static GoalPeriodSpan WeekOf(DateOnly day, GoalWeekStart weekStart)
    {
        DateOnly start = WeekStartOf(day, weekStart);
        return new GoalPeriodSpan(start, start.AddDays(6));
    }

    /// <summary>The period of <paramref name="period"/> that <paramref name="day"/> is in.</summary>
    /// <param name="createdOn">The day the goal was created (two-week periods count from its week).</param>
    public static GoalPeriodSpan PeriodOf(GoalPeriod period, DateOnly day, GoalWeekStart weekStart, DateOnly createdOn)
    {
        switch (period)
        {
            case GoalPeriod.Day:
                return new GoalPeriodSpan(day, day);
            case GoalPeriod.Week:
                return WeekOf(day, weekStart);
            case GoalPeriod.TwoWeeks:
            {
                DateOnly anchor = WeekStartOf(createdOn, weekStart);
                int weeks = (WeekStartOf(day, weekStart).DayNumber - anchor.DayNumber) / 7;
                int pairs = (int)Math.Floor(weeks / 2.0);
                DateOnly start = anchor.AddDays(pairs * 14);
                return new GoalPeriodSpan(start, start.AddDays(13));
            }
            default:
            {
                var start = new DateOnly(day.Year, day.Month, 1);
                return new GoalPeriodSpan(start, start.AddMonths(1).AddDays(-1));
            }
        }
    }

    /// <summary>The period just before <paramref name="span"/>.</summary>
    public static GoalPeriodSpan Previous(GoalPeriod period, GoalPeriodSpan span, GoalWeekStart weekStart, DateOnly createdOn)
    {
        return PeriodOf(period, span.Start.AddDays(-1), weekStart, createdOn);
    }
}
