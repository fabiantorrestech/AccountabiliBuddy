package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * Steps live in Goals (TD1, owner 2026-11-13): Today no longer has a steps ring, a steps goal is seen in Today's goals when it is
 * "Show on Today". These are the rules of the one-time "Steps now live in Goals" card: whether a Steps goal already exists, and the
 * goal one tap makes. Never made by itself.
 */
object GoalStepsRules {
    const val DEFAULT_DAILY_STEPS = 8000
    const val NAME = "Steps"
    const val ICON = "footprint"
    const val COLOR = "#16A34A"

    /** Whether a live goal other than Deep work counts steps (its unit is steps, or it has a Daily steps source). */
    fun hasStepsGoal(goals: List<Goal>): Boolean =
        goals.any { goal ->
            goal.isLive && !goal.isDeepWork && (goal.target.unit == GoalUnit.Steps || goal.sources.any { it.type == GoalSourceType.HealthSteps })
        }

    /** The first area (by place) that is live; null when there is none. */
    fun areaFor(lists: GoalLists): GoalArea? = lists.liveAreas.firstOrNull()

    /** The goal the card makes: [dailySteps] a day, counted from the phone's daily steps, Show on Today on. */
    fun newGoal(id: UUID, areaId: UUID, position: Double, now: OffsetDateTime, dailySteps: Int = DEFAULT_DAILY_STEPS): Goal =
        Goal(
            id = id,
            areaId = areaId,
            name = NAME,
            icon = ICON,
            color = COLOR,
            kind = GoalKind.Amount,
            target = GoalTarget(GoalPeriod.Day, dailySteps.coerceAtLeast(1).toDouble(), GoalUnit.Steps),
            sources = listOf(GoalSourceRule(UUID.randomUUID(), GoalSourceType.HealthSteps)),
            createdAt = now,
            position = position,
            editedAt = now,
            showOnToday = true,
        )
}
