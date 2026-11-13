package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime
import java.util.UUID

/**
 * The first run's Work area and Deep work goal, and what "untouched" means for them (the PC's `GoalSeeds`): an untouched seed never
 * wins over another copy, so a device installed later does not wipe what was changed elsewhere.
 */
object GoalSeeds {
    val WORK_AREA_ID: UUID = UUID.fromString("d33b0a11-6f0c-4e2a-9d51-0000000000a1")
    const val WORK_AREA_NAME = "Work"
    const val WORK_AREA_ICON = "work"
    const val WORK_AREA_COLOR = "#457B9D"
    const val DEEP_WORK_NAME = "Deep work"
    const val DEEP_WORK_ICON = "center_focus_strong"
    const val DEEP_WORK_COLOR = "#2A9D8F"

    /** Deep work's stored target: daily minutes without an amount. */
    val DEEP_WORK_STORED_TARGET = GoalTarget(GoalPeriod.Day, null, GoalUnit.Minutes)

    fun workArea(at: OffsetDateTime): GoalArea = GoalArea(WORK_AREA_ID, WORK_AREA_NAME, WORK_AREA_ICON, WORK_AREA_COLOR, 0.0, at)

    fun deepWork(at: OffsetDateTime): Goal = Goal(
        Goal.DEEP_WORK_ID, WORK_AREA_ID, DEEP_WORK_NAME, DEEP_WORK_ICON, DEEP_WORK_COLOR, GoalKind.Time, DEEP_WORK_STORED_TARGET,
        listOf(GoalSources.DEEP_WORK_RULE), at, 0.0, at, builtIn = true, showOnToday = true,
    )

    /** Whether a Work area is still the seed: live, its name, icon, colour and place, no unknown fields. */
    fun isPristine(area: GoalArea): Boolean =
        area.id == WORK_AREA_ID && area.isLive && area.name == WORK_AREA_NAME && area.icon == WORK_AREA_ICON &&
            area.color == WORK_AREA_COLOR && area.position == 0.0 && area.extra.isEmpty()

    /** Whether a Deep work part is still the seed's value with the empty-id stamp. */
    fun isPristine(goal: Goal, field: GoalField, stamp: FieldStamp): Boolean =
        goal.id == Goal.DEEP_WORK_ID && stamp.by == FieldStamp.NOBODY &&
            GoalFieldValues.canonical(goal, field) == GoalFieldValues.canonical(deepWork(goal.createdAt), field)
}
