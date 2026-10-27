using System.Runtime.CompilerServices;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The one place that says when a day is guarded (ADR 47): every guard (work-hours guard, Pre-Work, the work nudge, comms
/// windows, the poll speed, the day bar, the shutdown routine) reads the day's work windows from here and never the plan's
/// parts directly. What a day gives depends on its template's mode:
/// <list type="bullet">
/// <item>Schedule: the work parts (Watch and Comms), or the work window standing in for them; as the plan says.</item>
/// <item>Budget: waking time (the night's wake time to the next night's bedtime, from the sleep plan) minus the
/// non-productive windows the template lists, starting after its Pre-Work part, until the day's counted focus reaches the
/// budget. From that moment no window is left: the guard eases off exactly when the budget is met, never before.</item>
/// <item>Free: none, and no Comms or Pre-Work parts either.</item>
/// </list>
/// "Counted focus" for a Budget is the sessions the user ran, never ones added by hand in History, test
/// sessions, side tasks, breaks or planning, passed in as <c>focusedOn</c>; without it nothing is counted and a Budget day shows
/// all its available time. A Budget day whose plan has no Sleep part takes its night from <c>nightFallback</c> (the sleep
/// settings' weeknight or weekend night). All times are local wall-clock times.
/// </summary>
public static class DayWorkWindows
{
    /// <summary>Id of the stand-in part a Budget window is returned as; the same everywhere.</summary>
    public static Guid BudgetWindowPartId { get; } = Guid.Parse("5c7a2e10-0f01-4b3d-8f6e-1a9c40000f01");

    // The plan as the guards see it (Free and Budget templates without their work parts), per plan instance.
    private static readonly ConditionalWeakTable<DayPlan, DayPlan> Views = new();

    /// <summary>
    /// Every part the guards and the day bar see touching <paramref name="range"/>: the plan's parts (a Free day without its
    /// work and Pre-Work parts, a Budget day with only its non-productive windows and first Pre-Work) plus each Budget
    /// day's guarded windows as parts of the first Watch category, clipped to the range, in order.
    /// </summary>
    /// <param name="focusedOn">Counted focus on a date (Budget days); null = nothing counted.</param>
    /// <param name="nightFallback">The night before a date when a Budget day's plan has no Sleep part (the sleep settings' weeknight or weekend night); null = the whole day.</param>
    public static IReadOnlyList<PlannedPart> PartsIn(DayPlan plan, LocalWindow range, DateTime now, Func<DateOnly, TimeSpan>? focusedOn = null, Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        DayPlan view = ViewOf(plan);
        List<PlannedPart> parts = [.. view.PartsIn(range)];
        bool hasBudgetDay = plan.Templates.Any(template => template.Template.Mode == DayMode.Budget) || plan.OneOffDays.Any(day => day.Template.Mode == DayMode.Budget);
        if (hasBudgetDay && plan.Categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch) is DayCategory work)
        {
            for (DateOnly date = DateOnly.FromDateTime(range.Start).AddDays(-1); date <= DateOnly.FromDateTime(range.End).AddDays(1); date = date.AddDays(1))
            {
                foreach (LocalWindow window in BudgetWindows(plan, view, date, focusedOn, now, nightFallback))
                {
                    if (DayTimeline.Clip(window, range) is LocalWindow shown)
                    {
                        var part = new PlanPart(BudgetWindowPartId, work.Id, TimeOnly.FromDateTime(window.Start), TimeOnly.FromDateTime(window.End));
                        parts.Add(new PlannedPart(part, work, shown));
                    }
                }
            }
        }

        return parts.OrderBy(part => part.Window.Start).ThenBy(part => part.Window.End).ThenBy(part => part.Part.Id).ToList();
    }

    /// <summary>The part at <paramref name="localTime"/> as the guards see it, or null when nothing is planned then.</summary>
    public static PlannedPart? PartAt(DayPlan plan, DateTime localTime, Func<DateOnly, TimeSpan>? focusedOn = null, Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        return PartsIn(plan, new LocalWindow(localTime, localTime.AddMinutes(1)), localTime, focusedOn, nightFallback)
            .FirstOrDefault(part => part.Window.Contains(localTime));
    }

    /// <summary>
    /// The guarded work windows (work and Comms parts, a Budget day's windows) touching <paramref name="range"/>, cut to it,
    /// earliest first. This is what the former <c>DayTimeline.Planned</c> gave, now by mode.
    /// </summary>
    public static IReadOnlyList<PlannedFocus> WorkWindows(DayPlan plan, LocalWindow range, DateTime now, Func<DateOnly, TimeSpan>? focusedOn = null, Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        return PartsIn(plan, range, now, focusedOn, nightFallback)
            .Where(part => part.Category.Behavior.IsWork())
            .Select(part => new PlannedFocus(part.Name, part.Category.Color, part.Window))
            .ToList();
    }

    /// <summary>
    /// The guarded windows of one date (midnight to midnight; a Budget day's last window may run past midnight, which
    /// the next date's call shows cut), given that day's counted focus so far.
    /// </summary>
    public static IReadOnlyList<PlannedFocus> WorkWindows(DayPlan plan, DateOnly date, TimeSpan focusedSoFar, DateTime now, Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        return WorkWindows(plan, new LocalWindow(start, start.AddDays(1)), now, day => day == date ? focusedSoFar : TimeSpan.Zero, nightFallback);
    }

    /// <summary>
    /// How a Budget day is going: the budget left after <paramref name="focusedSoFar"/> and the available time left from
    /// <paramref name="now"/>, with "behind" when the available time can no longer hold the budget. Null when the date
    /// isn't a Budget day.
    /// </summary>
    public static BudgetPace? Pace(DayPlan plan, DateOnly date, TimeSpan focusedSoFar, DateTime now, Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        DayTemplate template = plan.TemplateFor(date);
        if (template.Mode != DayMode.Budget || template.BudgetMinutes is not int minutes)
        {
            return null;
        }

        TimeSpan left = TimeSpan.FromMinutes(minutes) - focusedSoFar;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        // Unmet windows, whatever was focused: the time still on offer is what lies ahead of now.
        TimeSpan available = BudgetWindows(plan, ViewOf(plan), date, null, now, nightFallback)
            .Select(window => DayTimeline.Clip(window, new LocalWindow(now, DateTime.MaxValue)))
            .OfType<LocalWindow>()
            .Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
        return new BudgetPace(left, available, left > TimeSpan.Zero && available < left);
    }

    // A Budget day's guarded windows: from the night's wake time (after the day's Pre-Work part) to the next night's
    // bedtime, minus every other part the guards see in that stretch. Once the counted focus meets the budget, only what
    // lies before now is left.
    private static IReadOnlyList<LocalWindow> BudgetWindows(DayPlan plan, DayPlan view, DateOnly date, Func<DateOnly, TimeSpan>? focusedOn, DateTime now, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        DayTemplate template = plan.TemplateFor(date);
        if (template.Mode != DayMode.Budget || template.BudgetMinutes is not > 0)
        {
            return [];
        }

        DateTime midnight = date.ToDateTime(TimeOnly.MinValue);
        DateTime start = (DayPlanSleep.SleepOf(template, plan) ?? nightFallback?.Invoke(date)) is SleepTimes night ? midnight + night.Wake.ToTimeSpan() : midnight;
        DateTime end = BedtimeOf(plan, date, nightFallback);
        PlanPart? warmUp = view.TemplateFor(date).Parts
            .FirstOrDefault(part => plan.Category(part.CategoryId)?.Behavior == CategoryBehavior.PreWork && !part.CrossesMidnight);
        if (warmUp is not null)
        {
            DateTime warmUpEnd = midnight + TimeSpan.FromMinutes(warmUp.EndMinutes);
            start = warmUpEnd > start ? warmUpEnd : start;
        }

        if (end <= start)
        {
            return [];
        }

        var stretch = new LocalWindow(start, end);
        IReadOnlyList<LocalWindow> windows = DayTimeline.Subtract([stretch], view.PartsIn(stretch).Select(part => part.Window));
        if (focusedOn is not null && focusedOn(date) >= TimeSpan.FromMinutes(template.BudgetMinutes.Value))
        {
            windows = windows.Select(window => DayTimeline.Clip(window, new LocalWindow(DateTime.MinValue, now))).OfType<LocalWindow>().ToList();
        }

        return windows;
    }

    // When the waking day ends: the next night's bedtime (the evening of the date, or past midnight for an early-hours
    // bedtime), else the sleep settings' night; the end of the date when neither has it.
    private static DateTime BedtimeOf(DayPlan plan, DateOnly date, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        DateTime nextMidnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if ((DayPlanSleep.NightBefore(plan, date.AddDays(1)) ?? nightFallback?.Invoke(date.AddDays(1))) is not SleepTimes next)
        {
            return nextMidnight;
        }

        bool evening = next.Wake <= next.Bedtime;
        return (evening ? nextMidnight.AddDays(-1) : nextMidnight) + next.Bedtime.ToTimeSpan();
    }

    private static DayPlan ViewOf(DayPlan plan)
    {
        return plan.Templates.Any(template => template.Template.Mode != DayMode.Schedule) || plan.OneOffDays.Any(day => day.Template.Mode != DayMode.Schedule)
            ? Views.GetValue(plan, Build)
            : plan;
    }

    private static DayPlan Build(DayPlan plan)
    {
        return plan with
        {
            Templates = plan.Templates.Select(template => template with { Template = Guarded(plan, template.Template) }).ToList(),
            OneOffDays = plan.OneOffDays.Select(day => day with { Template = Guarded(plan, day.Template) }).ToList(),
        };
    }

    // A Free day loses its work, Comms and Pre-Work parts; a Budget day its work and Comms parts and every Pre-Work part
    // but the first (the warm-up before the day's first window).
    private static DayTemplate Guarded(DayPlan plan, DayTemplate template)
    {
        if (template.Mode == DayMode.Schedule)
        {
            return template;
        }

        CategoryBehavior? BehaviorOf(PlanPart part) => plan.Category(part.CategoryId)?.Behavior;
        List<PlanPart> parts = template.Parts
            .Where(part => BehaviorOf(part) is CategoryBehavior behavior && !behavior.IsWork() && behavior != CategoryBehavior.PreWork)
            .ToList();
        PlanPart? warmUp = template.Mode == DayMode.Budget
            ? template.Parts.Where(part => BehaviorOf(part) == CategoryBehavior.PreWork && !part.CrossesMidnight).OrderBy(part => part.Start).FirstOrDefault()
            : null;
        if (warmUp is not null)
        {
            parts.Add(warmUp);
        }

        return template with { Parts = parts };
    }
}
