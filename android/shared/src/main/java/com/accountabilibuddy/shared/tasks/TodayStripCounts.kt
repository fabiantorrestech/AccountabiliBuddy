package com.accountabilibuddy.shared.tasks

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The Tasks page's Today strip (round 2, change note §2): counts only, never a timer. The words follow the PC's strip
 * (TodayStripViewModel.Update): parts at 0 are left out, This session never. The phone's board says "Reminders" where the PC's wider
 * strip says "Reminders due today", and has no Finished part.
 *
 * @property ready The open actions in This session.
 * @property queued The tasks in Queued.
 * @property reminders The open tasks with a reminder due today.
 * @property finished The tasks finished today.
 */
data class TodayStripCounts(val ready: Int, val queued: Int, val reminders: Int, val finished: Int) {
    companion object {
        /** The strip's counts for the task list as it is at [now]. */
        fun of(lists: TaskLists, now: OffsetDateTime, zone: ZoneId): TodayStripCounts {
            val live = lists.tasks.map { it.task }.filter { it.isLive }
            val today = now.atZoneSameInstant(zone).toLocalDate()
            return TodayStripCounts(
                ready = live.filter { it.bin == TaskBin.SESSION && !it.isDone }.sumOf { it.actionCount },
                queued = live.count { it.bin == TaskBin.QUEUED },
                reminders = live.count { !it.isDone && it.bin != TaskBin.DONE && remindsOn(it, today, zone) },
                finished = live.count { it.bin == TaskBin.DONE },
            )
        }

        /** The session part: "1/3" while a session runs ([done] of [total] tasks), "3 ready" before one, "empty" with nothing in it. */
        fun sessionPart(live: Boolean, done: Int, total: Int, ready: Int): String = when {
            live -> "$done/$total"
            ready > 0 -> "$ready ready"
            else -> "empty"
        }

        /** The phone's line: "This session 3 ready · Queued 2 · Reminders 1". */
        fun phoneText(session: String, queued: Int, reminders: Int): String = text(session, queued, reminders, 0, "Reminders ")

        /** The PC's line: "This session 3 ready · Queued 2 · Reminders due today 1 · Finished 1". */
        fun pcText(session: String, queued: Int, reminders: Int, finished: Int): String = text(session, queued, reminders, finished, "Reminders due today ")

        private fun text(session: String, queued: Int, reminders: Int, finished: Int, remindersLabel: String): String {
            val parts = mutableListOf("This session $session")
            if (queued > 0) parts += "Queued $queued"
            if (reminders > 0) parts += remindersLabel + reminders
            if (finished > 0) parts += "Finished $finished"
            return parts.joinToString(" · ")
        }

        // A reminder at a set time today, or one tied to the due date that falls on today (the day before, for "1 day before").
        private fun remindsOn(task: TaskItem, today: LocalDate, zone: ZoneId): Boolean = task.reminders.orEmpty().any { reminder ->
            when {
                reminder.at != null -> reminder.at.atZoneSameInstant(zone).toLocalDate() == today
                reminder.rule == TaskReminder.DAY_BEFORE_AT_NINE -> task.dueOn == today.plusDays(1)
                else -> task.dueOn == today
            }
        }
    }
}
