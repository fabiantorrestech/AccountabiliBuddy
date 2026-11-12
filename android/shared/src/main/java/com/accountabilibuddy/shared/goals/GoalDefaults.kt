package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * What a device's goals first run makes (brief §5.3) and what the built-in Deep work goal always is; the PC's `GoalDefaults` (App) with
 * the same fixed ids, names, icons and colours (Q378), so the two first-run items merge as one once goals sync (ADR 79, Proposed).
 *
 * Deep work's stored record keeps only what the user may change (name, icon, colour, place, Show on Today, extra tiles, notes). Its
 * targets are views: on the phone its daily target is the PC's daily goal from the sync files (ADR 29), read only; its weekly target
 * is the device's own.
 */
object GoalDefaults {
    /** The PC's `GoalDefaults.WorkAreaId`. */
    val WORK_AREA_ID: UUID = UUID.fromString("d33b0a11-6f0c-4e2a-9d51-0000000000a1")

    const val WORK_AREA_NAME = "Work"
    const val DEEP_WORK_NAME = "Deep work"

    /** Deep work's stored target: daily minutes without an amount (the view puts the PC's daily goal in its place). */
    val DEEP_WORK_STORED_TARGET = GoalTarget(GoalPeriod.Day, null, GoalUnit.Minutes)

    fun newWorkArea(now: OffsetDateTime): GoalArea = GoalArea(WORK_AREA_ID, WORK_AREA_NAME, "work", "#457B9D", 0.0, now)

    fun newDeepWork(areaId: UUID, now: OffsetDateTime): Goal = normalized(
        Goal(
            Goal.DEEP_WORK_ID, areaId, DEEP_WORK_NAME, "center_focus_strong", "#2A9D8F", GoalKind.Time, DEEP_WORK_STORED_TARGET,
            listOf(GoalSources.DEEP_WORK_RULE), now, 0.0, now, builtIn = true, showOnToday = true,
        ),
    )

    /** Deep work as it must be stored, whatever was asked: built in, focus only, never paused or deleted, targets as views. */
    fun normalized(deepWork: Goal): Goal = deepWork.copy(
        id = Goal.DEEP_WORK_ID,
        kind = GoalKind.Time,
        builtIn = true,
        target = DEEP_WORK_STORED_TARGET,
        secondTarget = null,
        sources = listOf(GoalSources.DEEP_WORK_RULE),
        pauses = null,
        deletedAt = null,
    )
}
