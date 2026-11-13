package com.accountabilibuddy.shared.tasks

import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime

/**
 * The phone's edits of a task's reminders, due date and devices (R7.4; the PC's TaskActions.AddReminder / RemoveReminder / SetDue, the
 * same rules). Pure: each gives the changed [TaskItem] (the store stamps the field through [TaskEdits]), or null when the change is
 * refused. The calendar copy (ADR 78's "Add new reminders to") is the PC's: the phone leaves [TaskItem.calendar] alone.
 */
object TaskReminderEdits {
    private val RELATIVE = setOf(TaskReminder.AT_START, TaskReminder.TEN_MINUTES_BEFORE, TaskReminder.ONE_HOUR_BEFORE, TaskReminder.DAY_BEFORE_AT_NINE)

    /**
     * Adds a reminder: one of the relative rules or [TaskReminder.ABSOLUTE] at [at]. Refused (null) at [TaskReminder.MAX_PER_TASK], for a
     * relative rule the task already has (or the same moment twice), for an unknown rule and for an absolute one without a moment.
     */
    fun add(task: TaskItem, reminderId: String, rule: String, at: OffsetDateTime?): TaskItem? {
        val known = rule in RELATIVE || (rule == TaskReminder.ABSOLUTE && at != null)
        if (!known) return null
        val reminders = task.reminders.orEmpty()
        val repeated = reminders.any { it.rule == rule && (rule != TaskReminder.ABSOLUTE || it.at?.toInstant() == at?.toInstant()) }
        if (reminders.size >= TaskReminder.MAX_PER_TASK || repeated || reminders.any { it.id == reminderId }) return null
        return task.copy(reminders = reminders + TaskReminder(reminderId, rule, if (rule == TaskReminder.ABSOLUTE) at else null))
    }

    /** Takes one reminder off a task (the last one leaves it without reminders); null when it has none by that id. */
    fun remove(task: TaskItem, reminderId: String): TaskItem? {
        if (task.reminders.orEmpty().none { it.id == reminderId }) return null
        val left = task.reminders.orEmpty().filter { it.id != reminderId }
        return task.copy(reminders = left.ifEmpty { null })
    }

    /** "Done by": a date, with a time or for the whole day ([time] null); [on] null removes it (and its time). */
    fun setDue(task: TaskItem, on: LocalDate?, time: LocalTime?): TaskItem = task.copy(dueOn = on, dueAt = if (on == null) null else time)

    /** Which devices the task syncs to: only [self] (a local task, ADR 72) or everywhere (null). */
    fun setDevices(task: TaskItem, onlyThisDevice: Boolean, self: String): TaskItem = task.copy(devices = if (onlyThisDevice) listOf(self.lowercase()) else null)
}
