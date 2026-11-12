namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// On track / Behind / Not due yet (brief §5.5), per kind:
/// <list type="bullet">
/// <item><b>Daily goals</b> (a daily target, any habit with a daily period): Behind once the past days of this week (on the
/// user's week start) missed more than <see cref="GoalTarget.MissesAllowed"/> (none by default); else On track.</item>
/// <item><b>"N of the period's days"</b> (and a weekly or monthly habit, N = its amount): Behind when the live days left are
/// fewer than the days still needed; Not due yet when nothing is done and there is still slack; else On track.</item>
/// <item><b>An amount per period</b>: at least: On track once met or level with the period's pace line (the amount spread over
/// its live days, the days before today done); Not due yet when nothing is due and nothing done; else Behind. At most: Behind
/// once over the cap.</item>
/// <item><b>A long-term target only</b> (or a level to reach): the Target tile's status against the pace line to the date.</item>
/// <item>A goal paused today is <see cref="GoalPeriodStatus.Paused"/> and leaves the This week counts.</item>
/// </list>
/// Deep work's weekly target is read the same way: pass the goal with <see cref="Goal.Target"/> set to its
/// <see cref="Goal.SecondTarget"/>.
/// </summary>
public static class GoalStatus
{
    /// <summary>The span the "This period" tile shows: the week for a daily goal, else the goal's own period.</summary>
    public static GoalPeriodSpan ThisPeriodSpan(Goal goal, DateOnly today, GoalWeekStart weekStart, TimeZoneInfo zone)
    {
        return goal.Target.Period == GoalPeriod.Day
            ? GoalPeriods.WeekOf(today, weekStart)
            : GoalPeriods.PeriodOf(goal.Target.Period, today, weekStart, goal.CreatedOn(zone));
    }

    /// <summary>"This period" for the goal today.</summary>
    public static GoalPeriodStatus ThisPeriod(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart)
    {
        if (goal.IsPausedOn(today))
        {
            return GoalPeriodStatus.Paused;
        }

        if (today < goal.CreatedOn(data.Zone))
        {
            return GoalPeriodStatus.NotDueYet;
        }

        GoalPeriodSpan span = ThisPeriodSpan(goal, today, weekStart, data.Zone);
        IReadOnlyDictionary<DateOnly, GoalDayState> days = GoalCounting.History(goal, data, today, span.Start);
        if (GoalCounting.CountsDays(goal) && goal.Target.Period == GoalPeriod.Day)
        {
            int missed = days.Values.Count(state => state.Day < today && state.Status is GoalDayStatus.Missed or GoalDayStatus.NotCounted);
            return missed > (goal.Target.MissesAllowed ?? 0) ? GoalPeriodStatus.Behind : GoalPeriodStatus.OnTrack;
        }

        if (GoalCounting.CountsDays(goal))
        {
            GoalPeriodResult result = Evaluate(goal, data, span, days, today, false);
            double needed = result.Required - result.Progress;
            if (needed <= 0)
            {
                return GoalPeriodStatus.OnTrack;
            }

            bool todayHit = days.TryGetValue(today, out GoalDayState? todayState) && todayState.Hit;
            int daysLeft = span.Days().Count(day => day > today && goal.IsAliveOn(day, data.Zone))
                + (goal.IsAliveOn(today, data.Zone) && !todayHit ? 1 : 0);
            if (daysLeft < needed)
            {
                return GoalPeriodStatus.Behind;
            }

            return result.Progress == 0 && daysLeft > needed ? GoalPeriodStatus.NotDueYet : GoalPeriodStatus.OnTrack;
        }

        if (goal.Target.Amount is null || goal.Target.Direction == GoalDirection.Reach)
        {
            GoalTargetProgress? target = Target(goal, data, today);
            return target is null ? GoalPeriodStatus.NotDueYet
                : target.Met ? GoalPeriodStatus.OnTrack
                : target.ExpectedToday is null ? GoalPeriodStatus.NotDueYet
                : target.Status;
        }

        GoalPeriodResult period = Evaluate(goal, data, span, days, today, false);
        if (goal.Target.Direction == GoalDirection.AtMost)
        {
            return period.Met ? GoalPeriodStatus.OnTrack : GoalPeriodStatus.Behind;
        }

        if (period.Met)
        {
            return GoalPeriodStatus.OnTrack;
        }

        int elapsed = span.Days().Count(day => day < today && goal.IsAliveOn(day, data.Zone));
        double expected = period.AliveDays == 0 ? 0 : period.Required * elapsed / period.AliveDays;
        if (expected <= 0 && period.Progress <= 0)
        {
            return GoalPeriodStatus.NotDueYet;
        }

        return period.Progress >= expected ? GoalPeriodStatus.OnTrack : GoalPeriodStatus.Behind;
    }

    /// <summary>How the goal did in <paramref name="span"/> (days after <paramref name="today"/> count as live but not done).</summary>
    public static GoalPeriodResult Period(Goal goal, GoalSourceData data, GoalPeriodSpan span, DateOnly today)
    {
        DateOnly last = span.End < today ? span.End : today;
        IReadOnlyDictionary<DateOnly, GoalDayState> days = span.Start > last ? new Dictionary<DateOnly, GoalDayState>()
            : GoalCounting.Days(goal, data, span.Start, last, today).ToDictionary(state => state.Day);
        return Evaluate(goal, data, span, days, today, GoalCounting.ReachesDown(goal, data));
    }

    /// <summary>The Target tile; null when the goal has no long-term amount.</summary>
    public static GoalTargetProgress? Target(Goal goal, GoalSourceData data, DateOnly today)
    {
        if (goal.Target.TargetAmount is not double target)
        {
            return null;
        }

        DateOnly created = goal.CreatedOn(data.Zone);
        List<GoalDayState> live = GoalCounting.History(goal, data, today).Values
            .Where(state => goal.IsAliveOn(state.Day, data.Zone))
            .OrderBy(state => state.Day)
            .ToList();
        double? fraction = null;
        if (goal.Target.TargetDate is DateOnly date)
        {
            int span = date.DayNumber - created.DayNumber + 1;
            int elapsed = today.DayNumber - created.DayNumber + 1;
            fraction = span <= 0 ? 1 : Math.Clamp((double)elapsed / span, 0, 1);
        }

        double current;
        double? start = null;
        double? expected;
        bool met;
        bool onPace;
        if (goal.Target.Direction == GoalDirection.Reach)
        {
            bool down = GoalCounting.ReachesDown(goal, data);
            start = GoalCounting.FirstValue(goal, data);
            GoalDayState? latest = live.LastOrDefault(state => state.HasValue);
            current = latest?.Value ?? start ?? 0;
            double from = start ?? current;
            expected = fraction is double part ? from + ((target - from) * part) : null;
            met = start is not null && (down ? current <= target : current >= target);
            onPace = expected is double line && (down ? current <= line : current >= line);
        }
        else
        {
            current = live.Sum(state => state.Value);
            expected = fraction is double part ? target * part : null;
            met = goal.Target.Direction == GoalDirection.AtLeast && current >= target;
            onPace = expected is double line && (goal.Target.Direction == GoalDirection.AtMost ? current <= line : current >= line);
        }

        GoalPeriodStatus status = met ? GoalPeriodStatus.OnTrack
            : goal.Target.TargetDate is not DateOnly due ? GoalPeriodStatus.NotDueYet
            : today > due ? GoalPeriodStatus.Behind
            : onPace ? GoalPeriodStatus.OnTrack
            : GoalPeriodStatus.Behind;
        return new GoalTargetProgress(current, target, expected, start, met, status);
    }

    internal static GoalPeriodResult Evaluate(
        Goal goal, GoalSourceData data, GoalPeriodSpan span, IReadOnlyDictionary<DateOnly, GoalDayState> days, DateOnly today, bool reachesDown)
    {
        int aliveDays = span.Days().Count(day => goal.IsAliveOn(day, data.Zone));
        List<GoalDayState> done = span.Days()
            .Where(day => day <= today && goal.IsAliveOn(day, data.Zone))
            .Select(day => days.TryGetValue(day, out GoalDayState? state) ? state : null)
            .OfType<GoalDayState>()
            .ToList();

        if (GoalCounting.CountsDays(goal))
        {
            int asked = goal.Target.Period == GoalPeriod.Day ? span.Length
                : goal.Target.WeeklyDays ?? (int)Math.Max(1, Math.Round(goal.Target.Amount ?? 1));
            int required = Math.Min(asked, aliveDays);
            int hits = done.Count(state => state.Hit);
            return new GoalPeriodResult(span, required, hits, required > 0 && hits >= required, required == 0, aliveDays);
        }

        if (goal.Target.Amount is not double amount || aliveDays == 0)
        {
            return new GoalPeriodResult(span, 0, done.Sum(state => state.Value), false, true, aliveDays);
        }

        double requiredAmount = amount * aliveDays / span.Length;
        if (goal.Target.Direction == GoalDirection.Reach)
        {
            GoalDayState? latest = done.LastOrDefault(state => state.HasValue);
            bool reached = latest is not null && (reachesDown ? latest.Value <= amount : latest.Value >= amount);
            return new GoalPeriodResult(span, amount, latest?.Value ?? 0, reached, false, aliveDays);
        }

        double progress = done.Sum(state => state.Value);
        bool met = goal.Target.Direction == GoalDirection.AtMost ? progress <= requiredAmount : progress >= requiredAmount;
        return new GoalPeriodResult(span, requiredAmount, progress, met, false, aliveDays);
    }
}
