package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import kotlinx.serialization.json.JsonObject

/**
 * Deleted goal items as the PC keeps them (ADR 79): a tombstone carries no text (name, notes, sources, reasons, decisions, unknown
 * fields), only what the merge needs. Deleting a goal touches neither its entries nor its reminders.
 */
object GoalTombstones {
    /** A target for a tombstone that came without one (a goal always has one). */
    val PLACEHOLDER_TARGET = GoalTarget(GoalPeriod.Day, null, GoalUnit.Count)

    private val NO_EXTRA = JsonObject(emptyMap())

    fun delete(goal: Goal, at: OffsetDateTime): Goal = clean(goal.copy(deletedAt = at, editedAt = at))

    fun clean(goal: Goal): Goal {
        if (goal.isLive) return goal
        return goal.copy(
            name = "", notes = null, sources = emptyList(), newerSources = emptyList(),
            target = goal.target.copy(pendingChange = null, extra = NO_EXTRA),
            secondTarget = null, trackTimeOnly = false, pauses = null, extraTiles = null, stamps = null, extra = NO_EXTRA,
        )
    }

    fun clean(area: GoalArea): GoalArea = if (area.isLive) area else area.copy(name = "", extra = NO_EXTRA)

    fun clean(entry: GoalEntry): GoalEntry = if (entry.isLive) entry else entry.copy(extra = NO_EXTRA)

    fun clean(reminder: GoalReminder): GoalReminder = if (reminder.isLive) reminder else reminder.copy(reason = null, extra = NO_EXTRA)

    fun clean(review: GoalReviewRecord): GoalReviewRecord =
        if (review.isLive) review else review.copy(decisions = emptyList(), newerDecisions = emptyList(), notes = null, snoozedUntil = null, extra = NO_EXTRA)
}
