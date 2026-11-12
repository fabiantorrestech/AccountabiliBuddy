package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * Which focus counts toward which goal (plan §3b 5, binding), from what the synced session data already carries and nothing
 * new; the PC's `GoalFocusAttribution`, rule for rule:
 * - **The rule:** all of a session's counted focus counts toward a label goal when any task of its checklist carries that label
 *   (the task's category id, or [GoalLabelId.idFor] its name), and toward a #tag goal when any task carries that tag (only where
 *   tags are counted: the PC). A session with no checklist counts only toward "all focus" (Deep work).
 * - **Pieces:** a synced session travels in pieces (time away cut out) and only the first carries the checklist. A piece with no
 *   checklist continues the session before it, from the same device, when neither was added by hand, they have the same
 *   profile name, it starts at or after that piece's end, and either the PC was off just before it (`offMinutesBefore`) or it
 *   starts within [CONTINUATION_GAP] of it. Two known edges (Q360), kept the same as the PC: a step-away of over an hour cuts
 *   the session on a receiver (P1), and a new checklist-less session with the same profile within an hour joins the one before (P2).
 * A minute counts once per goal even when several sessions or rules match it ([focusWindows] unions them).
 */
object GoalFocusAttribution {
    /** The longest gap between two synced pieces still read as one session stepped away from. */
    val CONTINUATION_GAP: Duration = Duration.ofMinutes(60)

    /**
     * One device's synced sessions, its pieces joined into sessions. Call it once per device: pieces from different devices are
     * never joined.
     *
     * @param deviceSessions The sessions of one device's file, any order; one without an end runs until [now].
     */
    fun fromSynced(deviceSessions: Iterable<GoalSyncedPiece>, now: Instant, zone: ZoneId): List<GoalFocusSession> {
        val sessions = mutableListOf<GoalFocusSession>()
        var windows: MutableList<GoalWindow>? = null
        var tasks: List<GoalSessionTask> = emptyList()
        var added = false
        var profile: String? = null
        var lastEnd: Instant? = null

        for (piece in deviceSessions.sortedBy { it.start }) {
            if (!continues(piece, windows != null, added, profile, lastEnd)) {
                windows?.let { sessions += GoalFocusSession(it, tasks, added) }
                windows = mutableListOf()
                tasks = piece.tasks ?: emptyList()
                added = piece.added
                profile = piece.profile
            }

            val end = piece.end ?: now
            if (end > piece.start) {
                windows!!.add(GoalWindow(local(piece.start, zone), local(end, zone)))
            }

            // An open piece (the running session) is the last of its session: nothing continues it.
            lastEnd = piece.end
        }

        windows?.let { sessions += GoalFocusSession(it, tasks, added) }
        return sessions
    }

    /** Whether [rule] takes [session]'s focus (an off rule takes nothing). [tagsAvailable] is false on the phone (plan §3b 5). */
    fun matches(session: GoalFocusSession, rule: GoalSourceRule, tagsAvailable: Boolean): Boolean {
        if (!rule.automatic) {
            return false
        }

        return when (rule.type) {
            GoalSourceType.FocusAll -> true
            GoalSourceType.FocusLabel -> rule.labelId != null && session.tasks.any { carriesLabel(it, rule.labelId) }
            GoalSourceType.FocusTag -> tagsAvailable && rule.tag != null &&
                session.tasks.any { task -> task.labels.orEmpty().any { label -> TaskLabels.same(label, rule.tag) } }
            else -> false
        }
    }

    /** The focus that counts toward a goal: the windows of every session any of its focus rules takes, each minute once, earliest first. */
    fun focusWindows(sessions: Iterable<GoalFocusSession>, rules: List<GoalSourceRule>, tagsAvailable: Boolean): List<GoalWindow> {
        val focusRules = rules.filter {
            it.type == GoalSourceType.FocusAll || it.type == GoalSourceType.FocusLabel || it.type == GoalSourceType.FocusTag
        }
        if (focusRules.isEmpty()) {
            return emptyList()
        }

        return GoalTimeline.union(sessions.filter { session -> focusRules.any { matches(session, it, tagsAvailable) } }.flatMap { it.windows })
    }

    /** A time as local wall-clock time in [zone] (the PC's `FocusDayBuilder.Local`). */
    fun local(time: Instant, zone: ZoneId): LocalDateTime = LocalDateTime.ofInstant(time, zone)

    private fun continues(piece: GoalSyncedPiece, hasOpenSession: Boolean, sessionAdded: Boolean, sessionProfile: String?, lastEnd: Instant?): Boolean {
        if (!hasOpenSession || piece.tasks != null || piece.added || sessionAdded) {
            return false
        }

        if (sessionProfile == null || piece.profile != sessionProfile) {
            return false
        }

        if (lastEnd == null || piece.start < lastEnd) {
            return false
        }

        return piece.offMinutesBefore != null || Duration.between(lastEnd, piece.start) <= CONTINUATION_GAP
    }

    private fun carriesLabel(task: GoalSessionTask, labelId: UUID): Boolean =
        task.categoryId == labelId || (task.category != null && task.category.trim().isNotEmpty() && GoalLabelId.idFor(task.category) == labelId)
}
