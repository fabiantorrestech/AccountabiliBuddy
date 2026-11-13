package com.accountabilibuddy.shared.tasks

import java.time.Duration
import java.time.OffsetDateTime

/**
 * "Hide the Shelf during a session" (Settings › Tasks › During a session, R7.4): the looser-change rule of the PC's session layout
 * settings (M7 task 1). Hiding is the stricter way and applies at once, also dropping a waiting change; showing the Shelf in a session
 * is the looser way: it waits the looser-change delay, is refused while a focus session the user started runs, never applies during
 * one, and has no "apply now". In ProductaBuddy mode it applies at once. The time is the trusted time, read by the caller.
 *
 * @property hide The Shelf hides behind "Show the Shelf" while a session runs.
 * @property showAt When a waiting "show it" applies; null for none.
 */
data class TaskHideShelf(val hide: Boolean = true, val showAt: OffsetDateTime? = null)

/** What asking for a setting did. */
enum class TaskHideShelfResult { Applied, Waiting, RefusedDuringSession, Unchanged }

object TaskHideShelfRule {
    /** Asks for the Shelf to be hidden ([hide] true) or shown ([hide] false) during a session. */
    fun set(state: TaskHideShelf, hide: Boolean, productaBuddy: Boolean, sessionRunning: Boolean, now: OffsetDateTime, delay: Duration): Pair<TaskHideShelf, TaskHideShelfResult> {
        if (hide) {
            // Stricter: at once; a waiting "show it" is dropped.
            val next = TaskHideShelf(true, null)
            return next to if (next == state) TaskHideShelfResult.Unchanged else TaskHideShelfResult.Applied
        }

        if (!state.hide) return state.copy(showAt = null) to TaskHideShelfResult.Unchanged
        if (productaBuddy) return TaskHideShelf(false, null) to TaskHideShelfResult.Applied
        if (sessionRunning) return state to TaskHideShelfResult.RefusedDuringSession
        // A wait already running keeps its time.
        if (state.showAt != null) return state to TaskHideShelfResult.Waiting
        return state.copy(showAt = now.plus(delay)) to TaskHideShelfResult.Waiting
    }

    /** A waiting "show it" whose time has come applies, when no session runs (or in ProductaBuddy mode). */
    fun settle(state: TaskHideShelf, productaBuddy: Boolean, sessionRunning: Boolean, now: OffsetDateTime): TaskHideShelf {
        val at = state.showAt ?: return state
        if ((sessionRunning && !productaBuddy) || now.toInstant().isBefore(at.toInstant())) return state
        return TaskHideShelf(false, null)
    }

    /** Cancel on a waiting change. */
    fun cancel(state: TaskHideShelf): TaskHideShelf = state.copy(showAt = null)
}
