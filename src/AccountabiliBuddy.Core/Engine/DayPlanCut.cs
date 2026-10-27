using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// How much of today's remaining work time (the day plan's Watch parts from now to midnight) a day plan change frees:
/// time that was work before the change and isn't after it. Work added elsewhere doesn't make up for it, so moving a
/// part later frees the time it no longer covers. Used by the work-hours guard (ADR 28), where such a change waits.
/// </summary>
public static class DayPlanCut
{
    /// <param name="focusedOn">Counted focus on a date, for Budget days (once met, their guarded windows are over); null = nothing counted.</param>
    public static TimeSpan FreedToday(DayPlan before, DayPlan after, DateTime localNow, Func<DateOnly, TimeSpan>? focusedOn = null)
    {
        var rest = new LocalWindow(localNow, localNow.Date.AddDays(1));
        if (rest.End <= rest.Start)
        {
            return TimeSpan.Zero;
        }

        IReadOnlyList<LocalWindow> was = Work(before, rest, localNow, focusedOn);
        IReadOnlyList<LocalWindow> stays = Work(after, rest, localNow, focusedOn);
        return DayTimeline.Subtract(was, stays).Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
    }

    private static IReadOnlyList<LocalWindow> Work(DayPlan plan, LocalWindow rest, DateTime localNow, Func<DateOnly, TimeSpan>? focusedOn)
    {
        return DayWorkWindows.WorkWindows(plan, rest, localNow, focusedOn).Select(focus => focus.Window).ToList();
    }
}
