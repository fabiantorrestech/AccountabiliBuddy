using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// How much of today's guarded work a day plan change frees (ADR 28, 47), measured through <see cref="DayWorkWindows"/>
/// from now to midnight in two ways, the larger one counting:
/// <list type="bullet">
/// <item>Time: the guarded windows that were guarded before the change and aren't after it. Work added elsewhere doesn't
/// make up for it, so moving a part later frees the time it no longer covers; so do Free, a shorter Schedule, longer
/// non-productive windows, re-mapping today's weekday and switching or moving the week of the repeating cycle.</item>
/// <item>Hours, when today is a Budget day before or after: the work left (the budget not yet focused, at most the
/// guarded time ahead). A lower budget frees hours without freeing any window, because a Budget day is guarded until its
/// budget is met.</item>
/// </list>
/// More hours, shorter non-productive windows, Free to Budget and added work free nothing. Used by the work-hours guard,
/// where a change that frees work waits.
/// </summary>
public static class DayPlanCut
{
    /// <param name="focusedOn">Counted focus on a date, for Budget days (once met, their guarded windows are over); null = nothing counted.</param>
    /// <param name="nightFallback">The night before a date for a Budget day with no Sleep part (the sleep settings' night); null = the whole day.</param>
    /// <param name="nightFallbackAfter">The same for the changed plan, when its own sleep would move the fallback (saving a plan re-derives the sleep settings' nights from it); null = <paramref name="nightFallback"/>.</param>
    public static TimeSpan FreedToday(DayPlan before, DayPlan after, DateTime localNow, Func<DateOnly, TimeSpan>? focusedOn = null, Func<DateOnly, SleepTimes?>? nightFallback = null, Func<DateOnly, SleepTimes?>? nightFallbackAfter = null)
    {
        var rest = new LocalWindow(localNow, localNow.Date.AddDays(1));
        if (rest.End <= rest.Start)
        {
            return TimeSpan.Zero;
        }

        IReadOnlyList<LocalWindow> was = Work(before, rest, localNow, focusedOn, nightFallback);
        IReadOnlyList<LocalWindow> stays = Work(after, rest, localNow, focusedOn, nightFallbackAfter ?? nightFallback);
        TimeSpan freed = Total(DayTimeline.Subtract(was, stays));

        DateOnly today = DateOnly.FromDateTime(localNow);
        if (before.TemplateFor(today).Mode == DayMode.Budget || after.TemplateFor(today).Mode == DayMode.Budget)
        {
            TimeSpan hoursFreed = WorkLeft(before, today, was, focusedOn) - WorkLeft(after, today, stays, focusedOn);
            freed = hoursFreed > freed ? hoursFreed : freed;
        }

        return freed;
    }

    private static IReadOnlyList<LocalWindow> Work(DayPlan plan, LocalWindow rest, DateTime localNow, Func<DateOnly, TimeSpan>? focusedOn, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        return DayWorkWindows.WorkWindows(plan, rest, localNow, focusedOn, nightFallback).Select(focus => focus.Window).ToList();
    }

    // The work still to do today: the guarded time ahead, and on a Budget day no more than the budget not yet focused
    // (the guard eases off when it's met, so more windows than hours left don't ask for more work).
    private static TimeSpan WorkLeft(DayPlan plan, DateOnly today, IReadOnlyList<LocalWindow> windows, Func<DateOnly, TimeSpan>? focusedOn)
    {
        TimeSpan available = Total(DayTimeline.Union(windows));
        DayTemplate template = plan.TemplateFor(today);
        if (template.Mode != DayMode.Budget || template.BudgetMinutes is not int minutes)
        {
            return available;
        }

        TimeSpan left = TimeSpan.FromMinutes(minutes) - (focusedOn?.Invoke(today) ?? TimeSpan.Zero);
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        return left < available ? left : available;
    }

    private static TimeSpan Total(IEnumerable<LocalWindow> windows)
    {
        return windows.Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
    }
}
