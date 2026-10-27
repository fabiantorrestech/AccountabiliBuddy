package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant

/**
 * A buzz the watch gives for the running phase: a minute before it ends, when it ends, when the daily goal is reached,
 * or when planned work starts with no session running.
 */
data class PhaseAlert(val at: Instant, val kind: Kind, val onBreak: Boolean) {
    enum class Kind { Warning, End, Goal, WorkStart, PlanningEnd }
}

/** When the watch buzzes for the running session's phase (focus or break): at its end, and a minute before when wanted. */
object PhaseAlerts {
    private val WARNING_BEFORE: Duration = Duration.ofMinutes(1)

    /**
     * The alerts still ahead of [now], soonest first; none for a session with no set end, or one that has ended. While the PC
     * plans (before its focus countdown) the only one is the planning stage's end; the focus alerts follow once planning ends and
     * the next snapshot brings the focus phase's real end.
     */
    fun due(running: RunningSession?, now: Instant, warning: Boolean): List<PhaseAlert> {
        if (running != null && running.isPlanning) {
            return listOfNotNull(running.planningEndsAt?.takeIf { it.isAfter(now) }?.let { PhaseAlert(it, PhaseAlert.Kind.PlanningEnd, onBreak = false) })
        }

        val endsAt = running?.endsAt?.takeIf { it.isAfter(now) } ?: return emptyList()
        val warnAt = endsAt.minus(WARNING_BEFORE)
        return listOfNotNull(
            PhaseAlert(warnAt, PhaseAlert.Kind.Warning, running.onBreak).takeIf { warning && warnAt.isAfter(now) },
            PhaseAlert(endsAt, PhaseAlert.Kind.End, running.onBreak),
        )
    }

    /**
     * When today's focus reaches the daily goal (ADR 29), while a focus session runs and before it ends: focus grows a
     * minute a minute from now. Null without a goal, when it's already met, on a break or when the session ends first.
     */
    /** The next work stretch's start while no session runs (the start nudge, [WorkStart]); null for none. */
    fun workStart(snapshot: WatchSnapshot?, now: Instant): PhaseAlert? =
        WorkStart.next(snapshot, now)?.let { PhaseAlert(it, PhaseAlert.Kind.WorkStart, onBreak = false) }

    fun goal(snapshot: WatchSnapshot?, now: Instant, zone: java.time.ZoneId): PhaseAlert? {
        val goal = snapshot?.goalMinutes?.takeIf { it > 0 } ?: return null
        val running = snapshot.running?.takeIf { !it.onBreak && !it.isPlanning && (it.endsAt == null || it.endsAt.isAfter(now)) } ?: return null
        val left = goal - snapshot.focusedMinutes(now, zone)
        if (left <= 0) {
            return null
        }

        val at = now.plus(Duration.ofMinutes(left.toLong()))
        return if (running.endsAt != null && running.endsAt.isBefore(at)) null else PhaseAlert(at, PhaseAlert.Kind.Goal, onBreak = false)
    }
}
