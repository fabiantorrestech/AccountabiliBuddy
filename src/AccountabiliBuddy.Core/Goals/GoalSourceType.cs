using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What a source rule counts (brief §5.1). Which device can measure each: <see cref="GoalSources.AvailabilityOf"/>.</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalSourceType>))]
public enum GoalSourceType
{
    /// <summary>All counted focus, whatever the tasks: the built-in Deep work goal's only source (not in the brief's list).</summary>
    FocusAll,

    /// <summary>Focus in sessions with a task carrying the label <see cref="GoalSourceRule.LabelId"/> (the coloured dot, ADR 76).</summary>
    FocusLabel,

    /// <summary>Focus in sessions with a task carrying the #tag <see cref="GoalSourceRule.Tag"/>.</summary>
    FocusTag,

    /// <summary>A task carries this goal. "Coming soon": tasks have no goal link until ADR 79 is accepted (plan §3b 2).</summary>
    GoalField,

    /// <summary>Ticked, logged or time added by hand.</summary>
    Hand,

    HealthSteps,

    /// <summary>"Coming soon" until ADR 81 is accepted: weight goals take hand entries only.</summary>
    HealthWeight,

    HealthSleep,

    /// <summary>Time in an app or category (<see cref="GoalSourceRule.Apps"/>), at most or at least.</summary>
    ScreenTime,

    /// <summary>Time in calendar events of a category (ADR 58's classification). PC only; never in focus totals.</summary>
    CalendarCategory,

    /// <summary>A linked Beeminder goal (<see cref="GoalSourceRule.BeeminderSlug"/>); read only (ADR 43).</summary>
    Beeminder,

    /// <summary>The milestone's own task list. "Coming soon" until ADR 79 is accepted (plan §3b 2).</summary>
    MilestoneTasks,
}
