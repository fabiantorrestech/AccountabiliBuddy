package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The focus session running on the PC: its profile, when this phase started and when it ends (null: no set end), and
 * its guided checklist (ADR 24): the current task, and how many of how many are done (0 of 0: none).
 */
data class RunningSession(
    val profile: String?,
    val startedAt: Instant,
    val endsAt: Instant?,
    val onBreak: Boolean,
    val task: String? = null,
    val tasksDone: Int = 0,
    val tasksTotal: Int = 0,
    /** The current task's place among the open tasks (1, 2, 3…, as numbered on the PC); 0 = unknown or none. */
    val taskNumber: Int = 0,
    /**
     * Set while the PC is in its planning stage (not focus time): when it began and when its time is up. [startedAt] and [endsAt]
     * are then the focus phase it leads to. Null from an older phone or PC, and outside planning.
     */
    val planningStartedAt: Instant? = null,
    val planningEndsAt: Instant? = null,
) {
    /** True while the PC is planning (before its focus countdown); its time keeps counting past [planningEndsAt] until it is answered. */
    val isPlanning: Boolean get() = planningEndsAt != null && !onBreak

    /**
     * Still running at [now]: before its end, with no set end, or while planning (which goes on past its time until the PC's
     * "Ready?" is answered, whatever the projected focus end says).
     */
    fun isLiveAt(now: Instant): Boolean = isPlanning || endsAt == null || endsAt.isAfter(now)

    /**
     * With the checklist's current task, as the PC's bar shows it ("Fix tests › Lint": the task picked on the PC, else
     * the first open one, with its subtask), and its progress.
     */
    fun withTasks(tasks: List<com.accountabilibuddy.shared.sync.SessionTaskItem>, currentId: String? = null): RunningSession {
        val shown = com.accountabilibuddy.shared.sync.SessionTaskItem.shown(tasks, currentId)
        val text = shown?.let { (task, subtask) -> if (subtask == null) task.title else "${task.title} › ${subtask.title}" }
        val number = shown?.let { (task, _) -> com.accountabilibuddy.shared.sync.SessionTaskItem.openInShownOrder(tasks).indexOfFirst { it.id == task.id } + 1 } ?: 0
        return copy(task = text, tasksDone = tasks.count { it.isDone }, tasksTotal = tasks.size, taskNumber = number)
    }
}

/** A device whose focus is in the snapshot (ADR 22), and how old its data is: shown on the watch's "where from" page. */
data class DataSource(val name: String, val dataAt: Instant)

/**
 * What the watch shows (ADR 21), as the phone sends it: [date]'s plan parts, the focus sessions that count (cut to the
 * day; one with no end runs until now), drift marks, planned minutes, the week, and the session running on the PC.
 * [linked] false tells the watch it's no longer linked; [wifiAllowed] and [relayAllowed] are the phone's "Wi-Fi straight
 * to the PC" and relay switches. [dataAt] is how old the PC's data is (the phone may build the snapshot later). Never
 * titles, addresses, reasons or notes.
 */
data class WatchSnapshot(
    val createdAt: Instant,
    val linked: Boolean,
    val pcName: String?,
    val date: LocalDate,
    val parts: List<FocusPart>,
    val sessions: List<FocusSession>,
    val drift: List<TimeWindow>,
    val plannedMinutes: Int,
    val week: List<DayTotal>,
    val running: RunningSession?,
    val wifiAllowed: Boolean = true,
    val dataAt: Instant = createdAt,
    val relayAllowed: Boolean = true,
    /** The devices whose focus is in it, with their data's age (the linked PC first); empty in snapshots from before. */
    val sources: List<DataSource> = emptyList(),
    /** The daily deep-work goal in minutes (ADR 29): the outer ring and a buzz when reached; null when the PC sent none. */
    val goalMinutes: Int? = null,
    /** Today's flexible lunch for the Lunch page (from the phone only); null from an older phone or the PC over Wi-Fi. */
    val lunch: WatchLunch? = null,
) {
    /** Today's focus as a share of the goal (ADR 29), 0 to 1; null without a goal. */
    fun goalFraction(now: Instant, zone: ZoneId): Float? =
        goalMinutes?.takeIf { it > 0 }?.let { (focusedMinutes(now, zone).toFloat() / it).coerceIn(0f, 1f) }

    /**
     * Focused on [date] up to [now]: each session cut to the day, one with no end until [now] (or the running session's
     * end, when that has passed), overlaps counted once.
     */
    fun focusedMinutes(now: Instant, zone: ZoneId): Int {
        val dayStart = date.atStartOfDay(zone).toInstant()
        val dayEnd = date.plusDays(1).atStartOfDay(zone).toInstant()
        val until = minOf(openUntil(now), dayEnd)
        val windows = sessions.mapNotNull { session ->
            val start = maxOf(session.start, dayStart)
            val end = minOf(session.end ?: until, until)
            if (end.isAfter(start)) TimeWindow(start, end) else null
        }
        return minutes(windows)
    }

    /** Where a session with no end stops on the rings: now, or the running session's end once that has passed. */
    fun openUntil(now: Instant): Instant {
        val planning = running?.takeIf { it.isPlanning }
        if (planning != null) {
            // Planning is not focus time: an open session counts for nothing until the PC's focus begins.
            return planning.planningStartedAt ?: Instant.MIN
        }

        return running?.endsAt?.let { minOf(it, now) } ?: now
    }

    companion object {
        /**
         * The snapshot to keep when [incoming] arrives: [stored] when its data is newer (e.g. from the PC over Wi-Fi while
         * the phone sends older data), with the phone's switches and lunch from [incoming]; otherwise [incoming].
         */
        fun newerOf(stored: WatchSnapshot?, incoming: WatchSnapshot): WatchSnapshot =
            if (stored != null && stored.dataAt.isAfter(incoming.dataAt)) {
                stored.copy(wifiAllowed = incoming.wifiAllowed, relayAllowed = incoming.relayAllowed, lunch = incoming.lunch)
            } else {
                incoming
            }

        /** Whole minutes of [windows], overlaps counted once (rounded to the nearest minute). */
        fun minutes(windows: List<TimeWindow>): Int {
            var seconds = 0L
            var reached: Instant? = null
            for (window in windows.sortedBy { it.start }) {
                val start = reached?.let { maxOf(it, window.start) } ?: window.start
                if (window.end.isAfter(start)) {
                    seconds += Duration.between(start, window.end).seconds
                }
                reached = reached?.let { maxOf(it, window.end) } ?: window.end
            }
            return ((seconds + 30) / 60).toInt()
        }
    }
}
