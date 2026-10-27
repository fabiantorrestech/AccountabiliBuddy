using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>Whether headphone auto-play is allowed right now under the "Only at certain times" settings.</summary>
public static class HeadphoneSchedule
{
    /// <summary>True with the switch off, or when any checked option matches <paramref name="now"/> (local time).</summary>
    public static bool AllowsNow(HeadphoneSettings settings, DayPlan plan, DateTime now, Func<DateOnly, TimeSpan>? focusedOn, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        if (!settings.OnlyAtTimes)
        {
            return true;
        }

        return (settings.DuringPlanWindow && InPlanWindow(settings, plan, now, focusedOn, nightFallback))
            || (settings.OnDaysAndHours && InDaysAndHours(settings, now));
    }

    /// <summary>The categories the plan window option uses: the chosen ones, or the Work category (first Watch category) by default.</summary>
    public static IReadOnlyList<Guid> ChosenCategoryIds(HeadphoneSettings settings, DayPlan plan)
    {
        if (settings.PlanCategoryIds is not null)
        {
            return settings.PlanCategoryIds;
        }

        DayCategory? work = plan.Categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch);
        return work is null ? [] : [work.Id];
    }

    private static bool InPlanWindow(HeadphoneSettings settings, DayPlan plan, DateTime now, Func<DateOnly, TimeSpan>? focusedOn, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        IReadOnlyList<Guid> chosen = ChosenCategoryIds(settings, plan);
        return DayWorkWindows.PartsIn(plan, new LocalWindow(now, now.AddMinutes(1)), now, focusedOn, nightFallback)
            .Any(part => part.Window.Contains(now) && chosen.Contains(part.Category.Id));
    }

    private static bool InDaysAndHours(HeadphoneSettings settings, DateTime now)
    {
        if (!settings.EffectiveDays.Contains(now.DayOfWeek))
        {
            return false;
        }

        TimeOnly time = TimeOnly.FromDateTime(now);
        TimeOnly start = settings.EffectiveStart;
        TimeOnly end = settings.EffectiveEnd;
        // An end before the start runs past midnight.
        return start <= end ? time >= start && time < end : time >= start || time < end;
    }
}
