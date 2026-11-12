using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// What the first run makes (brief §5.3) and what the built-in Deep work goal always is. Its record in "goals.items" keeps only what the
/// user may change (name, icon, colour, place, Show on Today, extra tiles, notes): it is always built in, a Time goal counting all
/// counted focus (its sources are locked to focus, plan §3b 6), never paused or deleted, and its targets are views
/// (<see cref="GoalsService.DeepWork"/>: the daily one is the "goal" document, the weekly one "goals.deepWork").
/// </summary>
public static class GoalDefaults
{
    /// <summary>The default area's id: the same on every device, so the first run's area merges as one item once goals sync (ADR 79).</summary>
    public static Guid WorkAreaId { get; } = Guid.Parse("d33b0a11-6f0c-4e2a-9d51-0000000000a1");

    /// <summary>Deep work's one source rule (all counted focus), with a fixed id for the same reason.</summary>
    public static Guid DeepWorkSourceId => GoalSources.DeepWorkRule.Id;

    public const string WorkAreaName = "Work";
    public const string DeepWorkName = "Deep work";

    /// <summary>
    /// What Deep work's stored record holds as its target: a daily minutes target without an amount. It is never read as the target;
    /// the view puts the "goal" document's minutes in its place.
    /// </summary>
    public static GoalTarget DeepWorkStoredTarget { get; } = new(GoalPeriod.Day, null, GoalUnit.Minutes);

    public static GoalSourceRule DeepWorkSource => GoalSources.DeepWorkRule;

    public static GoalArea NewWorkArea(DateTimeOffset now)
    {
        return new GoalArea(WorkAreaId, WorkAreaName, "work", "#457B9D", 0, now);
    }

    public static Goal NewDeepWork(Guid areaId, DateTimeOffset now)
    {
        return Normalized(new Goal(
            Goal.DeepWorkId, areaId, DeepWorkName, "center_focus_strong", "#2A9D8F", GoalKind.Time, DeepWorkStoredTarget,
            [DeepWorkSource], now, 0, now, BuiltIn: true, ShowOnToday: true));
    }

    /// <summary>Deep work as it must be stored, whatever was asked: built in, focus only, never paused or deleted, targets as views.</summary>
    public static Goal Normalized(Goal deepWork)
    {
        return deepWork with
        {
            Id = Goal.DeepWorkId,
            Kind = GoalKind.Time,
            BuiltIn = true,
            Target = DeepWorkStoredTarget,
            SecondTarget = null,
            Sources = [DeepWorkSource],
            Pauses = null,
            DeletedAt = null,
        };
    }
}
