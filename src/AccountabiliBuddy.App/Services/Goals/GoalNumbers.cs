using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The one number a goal row shows on the right and its bar's share (change note §4a "the one number that matters"), and its
/// status pill (amber Behind; a muted "Due Fri" while a weekly goal isn't due yet; Paused). Shared by the Goals list and Today's
/// goals so both say the same thing.
/// </summary>
public static class GoalNumbers
{
    /// <summary>"1h 10m / 2h", "8 / 15 min", "2 / 3", "6,240 / 8k", "182.4 lb", "4 / 7", "1h 10m today".</summary>
    public static string Number(GoalFacts facts)
    {
        Goal goal = facts.Goal;
        GoalTarget target = goal.Target;
        switch (goal.Kind)
        {
            case GoalKind.Habit:
                if (target.Period == GoalPeriod.Day)
                {
                    return facts.Threshold is double threshold
                        ? $"{(int)Math.Floor(facts.TodayState.Minutes)} / {GoalTexts.Minutes(threshold)}"
                        : facts.TodayState.Hit ? "Done" : "Not yet";
                }

                return $"{(int)facts.Period.Progress} / {(int)facts.Period.Required}";

            case GoalKind.Amount when target.Direction == GoalDirection.Reach:
            {
                double? latest = facts.Recent.LastOrDefault(state => state.HasValue)?.Value;
                return latest is double value ? GoalTexts.Value(value, target.Unit) : "No reading yet";
            }

            case GoalKind.Milestone when facts.Target is { } milestone:
                return $"{GoalTexts.Number(milestone.Current, 0)} / {GoalTexts.Number(milestone.Target, 0)}";
        }

        if (target.Amount is not double amount)
        {
            if (facts.Target is { } longTerm)
            {
                return $"{GoalTexts.Value(longTerm.Current, target.Unit)} / {GoalTexts.ShortValue(longTerm.Target, target.Unit)}";
            }

            return $"{GoalTexts.Value(facts.TodayState.Value, target.Unit)} today";
        }

        double done = target.Period == GoalPeriod.Day ? facts.TodayState.Value : facts.Period.Progress;
        return $"{GoalTexts.Value(done, target.Unit)} / {GoalTexts.ShortValue(amount, target.Unit)}";
    }

    /// <summary>The bar's share (0 to 1) for goals drawn with a bar; NaN when there is nothing to measure against.</summary>
    public static double Fraction(GoalFacts facts)
    {
        Goal goal = facts.Goal;
        if (goal.Kind == GoalKind.Milestone && facts.Target is { Target: > 0 } milestone)
        {
            return Math.Clamp(milestone.Current / milestone.Target, 0, 1);
        }

        if (goal.Target.Amount is double amount && amount > 0)
        {
            double done = goal.Target.Period == GoalPeriod.Day ? facts.TodayState.Value : facts.Period.Progress;
            return Math.Clamp(done / amount, 0, 1);
        }

        if (facts.Target is { Target: > 0 } target)
        {
            return Math.Clamp(target.Current / target.Target, 0, 1);
        }

        return double.NaN;
    }

    /// <summary>The row's visual for its kind.</summary>
    public static GoalRowVisual Visual(GoalFacts facts)
    {
        Goal goal = facts.Goal;
        GoalTarget target = goal.Target;
        return goal.Kind switch
        {
            GoalKind.Habit when target.Period == GoalPeriod.Day => GoalRowVisual.Strip,
            GoalKind.Habit when facts.PeriodCells.Count is > 0 and <= 7 => GoalRowVisual.WeekDots,
            GoalKind.Amount when target.Direction == GoalDirection.Reach => GoalRowVisual.Sparkline,
            GoalKind.Amount when target.Period == GoalPeriod.Day && target.Amount is not null => GoalRowVisual.DayBars,
            _ => double.IsNaN(Fraction(facts)) ? GoalRowVisual.None : GoalRowVisual.Bar,
        };
    }

    /// <summary>The status pill's text, or null for none; <paramref name="amber"/> for Behind.</summary>
    public static string? Pill(GoalFacts facts, out bool amber)
    {
        amber = false;
        if (facts.IsPaused)
        {
            return "Paused";
        }

        if (facts.Status == GoalPeriodStatus.Behind)
        {
            amber = true;
            return "Behind";
        }

        if (facts.Status == GoalPeriodStatus.NotDueYet && facts.Goal.Target.Period != GoalPeriod.Day && facts.HasPeriodTarget)
        {
            return $"Due {facts.PeriodSpan.End.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return null;
    }
}
