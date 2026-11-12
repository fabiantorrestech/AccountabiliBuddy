package com.accountabilibuddy.shared.goals

import java.time.ZoneId
import java.util.UUID

/**
 * What goals are counted from on one device ([GoalCounting]); the PC's `GoalSourceData`.
 *
 * @property focus The focus sessions across the user's devices ([GoalFocusAttribution.fromSynced] per device).
 * @property entries Goal entries (any goals'; each goal reads its own live ones).
 * @property zone The local time zone (dates, back-fills).
 * @property calendarWindows Calendar time per ADR 58 category (PC only, plan §3b 7); null = none. The phone never passes any.
 * @property tagsAvailable False where #tag rules aren't counted: the phone until ADR 79 (plan §3b 5; [forPhone]).
 */
data class GoalSourceData(
    val focus: List<GoalFocusSession>,
    val entries: List<GoalEntry>,
    val zone: ZoneId,
    val calendarWindows: Map<UUID, List<GoalWindow>>? = null,
    val tagsAvailable: Boolean = true,
) {
    companion object {
        /**
         * The phone's data: no #tag rules (plan §3b 5, "Measured on your PC") and no calendar time (plan §3b 7, PC only), so a
         * goal with those sources counts only its other sources here.
         */
        fun forPhone(focus: List<GoalFocusSession>, entries: List<GoalEntry>, zone: ZoneId): GoalSourceData =
            GoalSourceData(focus, entries, zone, calendarWindows = null, tagsAvailable = false)
    }
}
