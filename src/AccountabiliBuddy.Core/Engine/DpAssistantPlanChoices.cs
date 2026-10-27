using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// What the Setup DP Assistant's steps decide about the day plan (workdays are the weeks' Workday cells). The goal and the
/// sleep settings are saved by the app on their own; this is only the plan's part.
/// </summary>
/// <param name="WorkTime">How the Workday type describes work.</param>
/// <param name="WindowStart">The work window's start (<see cref="DpWorkTime.Window"/>).</param>
/// <param name="WindowEnd">The work window's end.</param>
/// <param name="BudgetMinutes">The budget's productive minutes (<see cref="DpWorkTime.Budget"/>).</param>
/// <param name="Lunch">A lunch window or a fixed lunch period.</param>
/// <param name="LunchWindowStart">The earliest lunch may start (<see cref="DpLunchKind.Flexible"/>).</param>
/// <param name="LunchWindowEnd">The latest lunch may start.</param>
/// <param name="LunchMinutes">How long a flexible lunch lasts once started.</param>
/// <param name="LunchHint">One hint if lunch hasn't started by this time.</param>
/// <param name="FixedLunchStart">When a fixed lunch starts by itself (<see cref="DpLunchKind.Fixed"/>).</param>
/// <param name="FixedLunchEnd">When a fixed lunch ends.</param>
/// <param name="WorkdayNight">The night before a workday; null = sleep is skipped and the plan's nights stay as they are.</param>
/// <param name="OtherNight">The night before any other day; null likewise.</param>
/// <param name="Weeks">The cycle's weeks (1 to <see cref="PlanCycle.MaxWeeks"/>), each a weekday-to-template map.</param>
/// <param name="StartsOn">The Monday week 1 starts on.</param>
public sealed record DpAssistantPlanChoices(
    DpWorkTime WorkTime,
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    int BudgetMinutes,
    DpLunchKind Lunch,
    TimeOnly LunchWindowStart,
    TimeOnly LunchWindowEnd,
    int LunchMinutes,
    TimeOnly LunchHint,
    TimeOnly FixedLunchStart,
    TimeOnly FixedLunchEnd,
    SleepTimes? WorkdayNight,
    SleepTimes? OtherNight,
    IReadOnlyList<IReadOnlyList<WeekdayTemplate>> Weeks,
    DateOnly StartsOn)
{
    /// <summary>The assistant's default work window, 9:00 AM to 5:00 PM.</summary>
    public static readonly (TimeOnly Start, TimeOnly End) DefaultWindow = (new TimeOnly(9, 0), new TimeOnly(17, 0));

    /// <summary>The assistant's default budget, 6 hours.</summary>
    public const int DefaultBudgetMinutes = 6 * 60;

    /// <summary>The default lunch window, 11:30 AM to 2:00 PM, 1 hour long once started, with a hint at 12:00 PM.</summary>
    public static readonly (TimeOnly Start, TimeOnly End, int Minutes, TimeOnly Hint) DefaultLunchWindow =
        (new TimeOnly(11, 30), new TimeOnly(14, 0), 60, new TimeOnly(12, 0));

    /// <summary>The default fixed lunch, 12:00 PM to 1:00 PM.</summary>
    public static readonly (TimeOnly Start, TimeOnly End) DefaultFixedLunch = (new TimeOnly(12, 0), new TimeOnly(13, 0));
}
