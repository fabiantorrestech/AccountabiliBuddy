package com.accountabilibuddy.shared.goals

import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * One thing that counts toward a goal (brief §5.1, "Tracking & sources"). Sources add up; a minute counts once per goal even
 * when several of its rules match it ([GoalCounting]). Only the fields of its [type] are set.
 *
 * @property labelId FocusLabel: the label's id ([GoalLabelId.idFor] its name).
 * @property tag FocusTag: the #tag, compared case-insensitively. Counted on the PC only until ADR 79 (plan §3b 5).
 * @property calendarCategoryId CalendarCategory: the ADR 58 category whose events count (PC only, plan §3b 7).
 * @property apps ScreenTime: the apps and sites that count ([GoalAppMatching]). Private text: sealed at rest, never logged. The older
 * single `app` text is read as one guessed entry when this is missing, and written as the first entry's text for older readers.
 * @property screenDirection ScreenTime: at most or at least.
 * @property beeminderSlug Beeminder: the linked goal's slug.
 * @property beeminderDirection Beeminder: Read only until ADR 80 (Send and Both are "Coming soon").
 * @property thresholdMinutes "Ticks itself at 15 min": a habit's day is hit once the time sources reach it; null = only a hand tick.
 * @property automatic Whether the source ticks and logs by itself. Off: it is listed but counts nothing.
 * @property devices ScreenTime: the device ids allowed to count; null = only [createdOn].
 * @property allDevices ScreenTime: every device counts ([devices] is then ignored).
 * @property createdOn The device that created the rule (stamped by the editor); null on rules written before.
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalSourceRule(
    val id: UUID,
    val type: GoalSourceType,
    val labelId: UUID? = null,
    val tag: String? = null,
    val calendarCategoryId: UUID? = null,
    val screenDirection: GoalDirection? = null,
    val beeminderSlug: String? = null,
    val beeminderDirection: GoalBeeminderDirection? = null,
    val thresholdMinutes: Double? = null,
    val automatic: Boolean = true,
    val apps: List<GoalAppMatch>? = null,
    val devices: List<UUID>? = null,
    val allDevices: Boolean = false,
    val createdOn: UUID? = null,
    val extra: JsonObject = JsonObject(emptyMap()),
) {
    /** The first entry's text, written for older readers. */
    val app: String?
        get() = apps?.firstOrNull()?.text

    /** Whether the rule counts time (focus or calendar windows) rather than readings or ticks. */
    val countsTime: Boolean
        get() = type == GoalSourceType.FocusAll || type == GoalSourceType.FocusLabel || type == GoalSourceType.FocusTag ||
            type == GoalSourceType.CalendarCategory
}
