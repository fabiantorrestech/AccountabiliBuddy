package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * One thing that counts toward a goal (brief §5.1, "Tracking & sources"). Sources add up; a minute counts once per goal even
 * when several of its rules match it ([GoalCounting]). Only the fields of its [type] are set.
 *
 * @property labelId FocusLabel: the label's id ([GoalLabelId.idFor] its name).
 * @property tag FocusTag: the #tag, compared case-insensitively. Counted on the PC only until ADR 79 (plan §3b 5).
 * @property calendarCategoryId CalendarCategory: the ADR 58 category whose events count (PC only, plan §3b 7).
 * @property app ScreenTime: the app or category name. Private text: sealed at rest, never logged.
 * @property screenDirection ScreenTime: at most or at least.
 * @property beeminderSlug Beeminder: the linked goal's slug.
 * @property beeminderDirection Beeminder: Read only until ADR 80 (Send and Both are "Coming soon").
 * @property thresholdMinutes "Ticks itself at 15 min": a habit's day is hit once the time sources reach it; null = only a hand tick.
 * @property automatic Whether the source ticks and logs by itself. Off: it is listed but counts nothing.
 */
data class GoalSourceRule(
    val id: UUID,
    val type: GoalSourceType,
    val labelId: UUID? = null,
    val tag: String? = null,
    val calendarCategoryId: UUID? = null,
    val app: String? = null,
    val screenDirection: GoalDirection? = null,
    val beeminderSlug: String? = null,
    val beeminderDirection: GoalBeeminderDirection? = null,
    val thresholdMinutes: Double? = null,
    val automatic: Boolean = true,
) {
    /** Whether the rule counts time (focus or calendar windows) rather than readings or ticks. */
    val countsTime: Boolean
        get() = type == GoalSourceType.FocusAll || type == GoalSourceType.FocusLabel || type == GoalSourceType.FocusTag ||
            type == GoalSourceType.CalendarCategory
}
