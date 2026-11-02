package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Instant
import java.util.UUID

/**
 * The step edits of a task, as pure functions on its list of steps (the same on the next session's list and on the Later list,
 * and as the sheet shows a running task's). A new step has an id at once, so a later edit can name it.
 */
object TaskSteps {
    /** The steps with a new open one at the end; a blank title adds nothing. */
    fun added(steps: List<SessionTaskItem>, title: String): List<SessionTaskItem> {
        val trimmed = title.trim()
        return if (trimmed.isEmpty()) steps else steps + SessionTaskItem(UUID.randomUUID().toString(), trimmed, null, emptyList())
    }

    /** The step [id] reworded; a blank title leaves it as it was. */
    fun renamed(steps: List<SessionTaskItem>, id: String?, title: String): List<SessionTaskItem> {
        val trimmed = title.trim()
        return if (trimmed.isEmpty()) steps else steps.map { if (it.id == id) it.copy(title = trimmed) else it }
    }

    /** The steps without [id]. */
    fun removed(steps: List<SessionTaskItem>, id: String?): List<SessionTaskItem> = steps.filter { it.id != id }

    /** The step [id] one place up ([by] -1) or down (+1); at an end nothing moves. */
    fun moved(steps: List<SessionTaskItem>, id: String?, by: Int): List<SessionTaskItem> {
        val from = steps.indexOfFirst { it.id == id }
        val to = from + by
        if (from < 0 || to !in steps.indices) return steps
        return steps.toMutableList().apply { add(to, removeAt(from)) }
    }

    /** The step [id] ticked at [now], or open again when it was ticked. */
    fun toggled(steps: List<SessionTaskItem>, id: String?, now: Instant): List<SessionTaskItem> =
        steps.map { if (it.id == id) it.copy(doneAt = if (it.isDone) null else now) else it }

    /** The steps still open. */
    fun open(steps: List<SessionTaskItem>?): List<SessionTaskItem> = steps.orEmpty().filter { !it.isDone }

    /** The open steps as new, open steps with new ids (a task that moves to another list starts its steps afresh, as on the PC). */
    fun reopened(steps: List<SessionTaskItem>?): List<SessionTaskItem> =
        open(steps).map { SessionTaskItem(UUID.randomUUID().toString(), it.title, null, emptyList()) }
}
