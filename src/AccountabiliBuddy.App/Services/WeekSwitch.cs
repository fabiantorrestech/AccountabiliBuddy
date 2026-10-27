using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The "This week is A / B" switch (ADR 47): which week alternating weeks are in, and the plan that has a given week be A or B
/// by moving the Monday week B starts on.
/// </summary>
public static class WeekSwitch
{
    /// <summary>True when alternating weeks are on (a week B is set).</summary>
    public static bool IsAlternating(DayPlan plan)
    {
        return plan.Week.B is not null && plan.Week.BStartsOn is not null;
    }

    /// <summary>The Monday of the week <paramref name="date"/> is in.</summary>
    public static DateOnly MondayOf(DateOnly date)
    {
        return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    /// <summary>
    /// The plan with the week <paramref name="date"/> is in made week B (or week A): week B's start moves to that week's
    /// Monday, or the Monday a week before it, so the weeks keep alternating around it. The same edit the phone's switch
    /// makes (DayPlanEditing.withThisWeek). Unchanged when alternating weeks are off or the week already is that one.
    /// </summary>
    public static DayPlan WithThisWeek(DayPlan plan, DateOnly date, bool weekB)
    {
        if (!IsAlternating(plan) || plan.Week.IsWeekB(date) == weekB)
        {
            return plan;
        }

        DateOnly monday = MondayOf(date);
        return plan with { Week = plan.Week with { BStartsOn = weekB ? monday : monday.AddDays(-7) } };
    }
}
