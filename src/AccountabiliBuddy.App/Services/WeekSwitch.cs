using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The "This is week N of M" switch (ADR 47): which week of the plan's repeating cycle this week is, and the plan that has
/// a given week be week N by moving the Monday week 1 starts on by whole weeks.
/// </summary>
public static class WeekSwitch
{
    /// <summary>True when the plan's weeks repeat in a cycle of more than one week (there is a week to switch to).</summary>
    public static bool IsRepeating(DayPlan plan)
    {
        return plan.Cycle.Weeks.Count > 1;
    }

    /// <summary>The Monday of the week <paramref name="date"/> is in.</summary>
    public static DateOnly MondayOf(DateOnly date)
    {
        return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    /// <summary>
    /// The plan with the week <paramref name="date"/> is in made week <paramref name="week"/> (1-based) of the cycle: week
    /// 1's start moves to the Monday <c>week - 1</c> weeks before that week's Monday, so the weeks keep repeating around it.
    /// The same edit the phone's switch makes (DayPlanEditing.withThisWeek). Unchanged when the weeks don't repeat, the
    /// week isn't one of the cycle's, or the week already is that one.
    /// </summary>
    public static DayPlan WithThisWeek(DayPlan plan, DateOnly date, int week)
    {
        if (!IsRepeating(plan) || week < 1 || week > plan.Cycle.Weeks.Count || plan.Cycle.WeekNumberFor(date) == week)
        {
            return plan;
        }

        return plan with { Cycle = plan.Cycle with { StartsOn = MondayOf(date).AddDays(-7 * (week - 1)) } };
    }
}
