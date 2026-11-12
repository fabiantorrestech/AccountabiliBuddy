package com.accountabilibuddy.shared.goals

/** What a source rule counts (brief §5.1). Which device can measure each: [GoalSources.availabilityOf]. */
enum class GoalSourceType {
    /** All counted focus, whatever the tasks: the built-in Deep work goal's only source. */
    FocusAll,

    /** Focus in sessions with a task carrying the label [GoalSourceRule.labelId] (the coloured dot, ADR 76). */
    FocusLabel,

    /** Focus in sessions with a task carrying the #tag [GoalSourceRule.tag]. Counted on the PC only until ADR 79 (plan §3b 5). */
    FocusTag,

    /** A task carries this goal. "Coming soon": tasks have no goal link until ADR 79 is accepted (plan §3b 2). */
    GoalField,

    /** Ticked, logged or time added by hand. */
    Hand,

    HealthSteps,

    /** "Coming soon" until ADR 81 is accepted: weight goals take hand entries only. */
    HealthWeight,

    HealthSleep,

    /** Time in an app or category ([GoalSourceRule.apps]), at most or at least. */
    ScreenTime,

    /** Time in calendar events of a category (ADR 58's classification). PC only (plan §3b 7); never in focus totals. */
    CalendarCategory,

    /** A linked Beeminder goal ([GoalSourceRule.beeminderSlug]); read only (ADR 43), on the PC. */
    Beeminder,

    /** The milestone's own task list. "Coming soon" until ADR 79 is accepted (plan §3b 2). */
    MilestoneTasks,
}
