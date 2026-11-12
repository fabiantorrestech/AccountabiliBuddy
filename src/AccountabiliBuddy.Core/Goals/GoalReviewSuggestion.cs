namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The weekly review's suggestion for a goal (brief §5.8), rule-based on the device, with the facts it used as pills:
/// <list type="number">
/// <item><b>Pause</b> when nothing counted for <see cref="QuietDays"/> days (the goal at least that old; paused days left out).
/// Checked first: a goal with no activity is also behind, and the brief's "Adjust" would otherwise always hide it.</item>
/// <item><b>Adjust the target</b> when it was behind (not met) in 2 of its last 3 periods that asked for something, or when the
/// projected finish at the current pace is more than 10% of the days left past the target date (no pace at all counts as
/// past it).</item>
/// <item><b>Keep going</b> otherwise.</item>
/// </list>
/// No network, no AI. "Adjust" opens Edit goal › Target pre-filled; a lowering still waits (<see cref="GoalLowering"/>).
/// </summary>
/// <param name="BehindPeriods">Of <paramref name="PeriodsChecked"/> (up to 3) completed periods, how many weren't met.</param>
/// <param name="BehindDays">Days missed or short in the last completed period.</param>
/// <param name="ChangePercent30">The last 30 days against the 30 before, in percent; null when the 30 before had nothing.</param>
/// <param name="PacePerDay">The trailing pace (<see cref="GoalWhatItTakes.Pace"/>); null with none.</param>
/// <param name="ProjectedFinish">"At this pace: &lt;date&gt;" for a goal with a long-term target; null otherwise.</param>
public sealed record GoalReviewSuggestion(
    GoalReviewChoice Choice,
    int BehindPeriods,
    int PeriodsChecked,
    int BehindDays,
    double? ChangePercent30,
    double? PacePerDay,
    DateOnly? ProjectedFinish,
    bool Quiet)
{
    /// <summary>Three weeks without activity suggests Pause.</summary>
    public const int QuietDays = 21;

    /// <summary>The projected finish may run this share of the days left past the date before Adjust is suggested.</summary>
    public const double LateShare = 0.10;

    public static GoalReviewSuggestion For(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart)
    {
        DateOnly created = goal.CreatedOn(data.Zone);
        IReadOnlyDictionary<DateOnly, GoalDayState> days = GoalCounting.History(goal, data, today);
        bool reachesDown = GoalCounting.ReachesDown(goal, data);

        // The last 3 completed periods that asked for something, newest first.
        var checkedPeriods = new List<GoalPeriodResult>();
        GoalPeriodSpan span = GoalPeriods.PeriodOf(goal.Target.Period, today, weekStart, created);
        for (span = GoalPeriods.Previous(goal.Target.Period, span, weekStart, created); span.End >= created && checkedPeriods.Count < 3; span = GoalPeriods.Previous(goal.Target.Period, span, weekStart, created))
        {
            GoalPeriodResult result = GoalStatus.Evaluate(goal, data, span, days, today, reachesDown);
            if (!result.Skipped)
            {
                checkedPeriods.Add(result);
            }
        }

        int behindPeriods = checkedPeriods.Count(result => !result.Met);
        int behindDays = checkedPeriods.Count == 0 ? 0
            : checkedPeriods[0].Span.Days().Count(day => days.TryGetValue(day, out GoalDayState? state) && state.Status is GoalDayStatus.Missed or GoalDayStatus.NotCounted);

        List<DateOnly> recentLive = Enumerable.Range(1, QuietDays)
            .Select(back => today.AddDays(-back))
            .Where(day => goal.IsAliveOn(day, data.Zone))
            .ToList();
        bool quiet = created.DayNumber <= today.DayNumber - QuietDays
            && recentLive.Count > 0
            && recentLive.All(day => !days.TryGetValue(day, out GoalDayState? state) || !Active(state));

        double recent = SumLive(goal, data, days, today.AddDays(-30), today.AddDays(-1));
        double before = SumLive(goal, data, days, today.AddDays(-60), today.AddDays(-31));
        double? change = before > 0 ? (recent - before) / before * 100 : null;

        GoalWhatItTakes? takes = GoalWhatItTakes.For(goal, data, today);
        double? pace = takes?.PacePerDay ?? GoalWhatItTakes.Pace(goal, data, today, GoalWhatItTakes.DefaultPaceWeeks, reachesDown);
        bool late = false;
        if (takes is { Verdict: GoalWhatItTakesVerdict.Covered or GoalWhatItTakesVerdict.NeedsMore, DaysLeft: int left } && goal.Target.TargetDate is DateOnly date)
        {
            int allowed = (int)Math.Ceiling(left * LateShare);
            late = takes.ProjectedFinish is not DateOnly finish || finish.DayNumber > date.DayNumber + allowed;
        }

        GoalReviewChoice choice = quiet ? GoalReviewChoice.Pause
            : behindPeriods >= 2 || late ? GoalReviewChoice.Adjust
            : GoalReviewChoice.KeepGoing;
        return new GoalReviewSuggestion(choice, behindPeriods, checkedPeriods.Count, behindDays, change, pace, takes?.ProjectedFinish, quiet);
    }

    private static bool Active(GoalDayState state)
    {
        return state.HasValue && (state.Value > 0 || state.Minutes > 0);
    }

    private static double SumLive(Goal goal, GoalSourceData data, IReadOnlyDictionary<DateOnly, GoalDayState> days, DateOnly from, DateOnly to)
    {
        double sum = 0;
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            if (goal.IsAliveOn(day, data.Zone) && days.TryGetValue(day, out GoalDayState? state))
            {
                sum += state.Value;
            }
        }

        return sum;
    }
}
