package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.OffsetDateTime

/**
 * Changes and the looser-change rule for goals (brief §5.7, §5.10), the PC's `GoalLowering`:
 * - Raising a target, or adding one, applies at once.
 * - Lowering the built-in Deep work goal's ([Goal.isDeepWork]) daily or weekly target, or removing it ("Track time only"), waits
 *   the looser-change delay and never applies during a session; the existing work-hours rule ([decide]'s `lowerWaitsNow`) holds too.
 * - In ProductaBuddy mode lowering Deep work skips the delay and the session rule but keeps the work-hours rule (Q362); such a
 *   wait is marked [GoalPendingChange.madeInProductaBuddy] and waits the full delay once AccountabiliBuddy mode sees it.
 * - Every other goal feeds no guard, so lowering it applies at once in both modes.
 * Goal counts never feed a guard, the goal unlock or `focusDay`. On the phone Deep work's daily target isn't edited at all
 * ([GoalPhoneRules.canEditDailyTarget]).
 */
object GoalLowering {
    /** What happens when [target]'s amount is changed to [newAmount] (null = no target). */
    fun decide(
        goal: Goal,
        target: GoalTarget,
        newAmount: Double?,
        productaBuddy: Boolean,
        lowerWaitsNow: Boolean,
        now: OffsetDateTime,
        looserDelay: Duration,
    ): GoalChangeDecision {
        if (sameAmount(target.amount, newAmount)) {
            return GoalChangeDecision(GoalChangeOutcome.Unchanged)
        }

        // Deep work by its flag or its fixed id: a record that lost the flag still lowers by the guarded rule.
        val guarded = goal.isDeepWork
        if (!isLooser(target.direction, target.amount, newAmount) || !guarded) {
            return GoalChangeDecision(GoalChangeOutcome.ApplyNow)
        }

        if (productaBuddy) {
            return if (lowerWaitsNow) {
                GoalChangeDecision(GoalChangeOutcome.Wait, now, madeInProductaBuddy = true)
            } else {
                GoalChangeDecision(GoalChangeOutcome.ApplyNow)
            }
        }

        return GoalChangeDecision(GoalChangeOutcome.Wait, now.plus(looserDelay))
    }

    /**
     * Whether a waiting change may apply now: its time has come, no session runs (outside ProductaBuddy mode) and the work-hours
     * rule doesn't hold it. One made in ProductaBuddy mode never applies in AccountabiliBuddy mode until [intoAccountabiliBuddy]
     * has restarted its wait.
     */
    fun canApply(pending: GoalPendingChange, now: OffsetDateTime, sessionRunning: Boolean, productaBuddy: Boolean, lowerWaitsNow: Boolean): Boolean {
        if (pending.madeInProductaBuddy && !productaBuddy) {
            return false
        }

        return !now.toInstant().isBefore(pending.appliesAt.toInstant()) && (productaBuddy || !sessionRunning) && !lowerWaitsNow
    }

    /**
     * A waiting change seen in AccountabiliBuddy mode: one made in ProductaBuddy mode waits the full delay from now (and is an
     * AccountabiliBuddy-mode change from then on); one made in AccountabiliBuddy mode keeps its time, never moved earlier.
     */
    fun intoAccountabiliBuddy(pending: GoalPendingChange, now: OffsetDateTime, looserDelay: Duration): GoalPendingChange {
        if (!pending.madeInProductaBuddy) {
            return pending
        }

        val earliest = now.plus(looserDelay)
        val appliesAt = if (pending.appliesAt.toInstant().isAfter(earliest.toInstant())) pending.appliesAt else earliest
        return pending.copy(appliesAt = appliesAt, madeInProductaBuddy = false)
    }

    /**
     * Whether going from [from] to [to] is looser: removing a target, a lower at-least amount or a higher at-most cap. Adding a
     * target is stricter; moving a level to reach is neither (it applies at once).
     */
    fun isLooser(direction: GoalDirection, from: Double?, to: Double?): Boolean {
        if (sameAmount(from, to) || from == null) {
            return false
        }

        if (to == null) {
            return true
        }

        return when (direction) {
            GoalDirection.AtLeast -> to < from
            GoalDirection.AtMost -> to > from
            GoalDirection.Reach -> false
        }
    }

    /** Pausing and deleting apply at once for every goal but Deep work, which can only switch to "Track time only". */
    fun canPauseOrDelete(goal: Goal): Boolean = !goal.isDeepWork

    // .NET's Nullable.Equals for doubles: both missing, or the same value (NaN equals NaN, 0 equals -0).
    private fun sameAmount(first: Double?, second: Double?): Boolean {
        if (first == null || second == null) {
            return first == null && second == null
        }

        val a: Double = first
        val b: Double = second
        return a == b || (a.isNaN() && b.isNaN())
    }
}
