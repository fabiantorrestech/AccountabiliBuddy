package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale

/**
 * The words of a task row on the phone (round 2 R7.3, boards Tasks-Phone and Tasks-Phone-Label): the detail line's parts in the
 * boards' order (the bin when the page isn't one, the label, the time limit, steps, the due date or "Overdue 3 d", the first reminder,
 * "untouched 4 wk", where it came from, then the tags), leaving out what a task doesn't have. Times are 12-hour with AM/PM, dates
 * "Fri 11/13" (with the year when it isn't this one).
 */
object TaskRowText {
    private val day: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)
    private val dayWithYear: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d/yyyy", Locale.US)
    private val clock: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /**
     * The detail line of [task]. [labelName] is its label's name (null for none), [showBin] adds the bin first (pages that aren't
     * bins), [showLabel] can leave the label out (a label's own page).
     */
    fun detail(task: TaskItem, labelName: String?, now: OffsetDateTime, zone: ZoneId, showBin: Boolean, showLabel: Boolean = true): List<TaskRowPart> {
        val local = now.atZoneSameInstant(zone)
        val today = local.toLocalDate()
        val parts = ArrayList<TaskRowPart>()
        if (showBin) parts += TaskRowPart(TaskBins.rowPart(task.bin))
        if (showLabel && labelName != null) parts += TaskRowPart(labelName)
        task.estimateMinutes?.let { parts += TaskRowPart(minutes(it)) }
        steps(task)?.let { parts += TaskRowPart(it) }
        if (task.dueOn != null) {
            parts += if (TaskViewRules.isOverdue(task, today, local.toLocalTime())) TaskRowPart(overdue(task.dueOn, today), late = true) else TaskRowPart(due(task.dueOn, task.dueAt, today))
        }

        reminder(task, today, zone)?.let { parts += TaskRowPart(it) }
        if (TaskBins.isShelf(task.bin) && TaskUntouched.isOlderThan(task, TaskUntouched.DEFAULT_WEEKS, now)) {
            parts += TaskRowPart("untouched ${TaskUntouched.weeks(task, now)} wk")
        }

        task.origin?.takeIf { it.isNotBlank() && it != TaskOrigins.FROM_PHONE }?.let { parts += TaskRowPart(it) }
        if (!task.tags.isNullOrEmpty()) parts += TaskRowPart(TaskLabels.text(task.tags))
        return parts
    }

    /** "50 min". */
    fun minutes(value: Int): String = "$value min"

    /** "1/3" for a task with steps; null without. */
    fun steps(task: TaskItem): String? = task.steps?.takeIf { it.isNotEmpty() }?.let { steps -> "${steps.count { it.isDone }}/${steps.size}" }

    /** "Fri 11/13", "Fri 11/13 3:00 PM", "Sun 1/31/2027". */
    fun due(on: LocalDate, at: LocalTime?, today: LocalDate): String =
        (if (on.year == today.year) day else dayWithYear).format(on) + (if (at == null) "" else " " + clock.format(at))

    /** "4:05 PM": a time of day, 12-hour with AM/PM. */
    fun time(at: LocalTime): String = clock.format(at)

    /** "Overdue 3 d"; "Overdue" for one due earlier today. */
    fun overdue(on: LocalDate, today: LocalDate): String {
        val days = ChronoUnit.DAYS.between(on, today)
        return if (days <= 0) "Overdue" else "Overdue $days d"
    }

    /** The first reminder in words ("10 min before", "Fri 11/13 3:00 PM"), with "+1" for more; null without reminders. */
    fun reminder(task: TaskItem, today: LocalDate, zone: ZoneId): String? {
        val reminders = task.reminders.orEmpty()
        val first = reminders.firstOrNull() ?: return null
        val text = when (first.rule) {
            TaskReminder.AT_START -> "at the time"
            TaskReminder.TEN_MINUTES_BEFORE -> "10 min before"
            TaskReminder.ONE_HOUR_BEFORE -> "1 hr before"
            TaskReminder.DAY_BEFORE_AT_NINE -> "the day before, 9:00 AM"
            else -> first.at?.atZoneSameInstant(zone)?.let { due(it.toLocalDate(), it.toLocalTime(), today) } ?: "reminder"
        }
        return if (reminders.size > 1) "$text +${reminders.size - 1}" else text
    }

    /** A sheet row's value for the reminders: "1 set", "None". */
    fun remindersSet(task: TaskItem): String = task.reminders.orEmpty().size.let { if (it == 0) "None" else "$it set" }

    /** A sheet row's value for the devices a task syncs to: "All devices", "This phone only", "2 devices". */
    fun devices(task: TaskItem, self: String): String {
        val devices = task.devices ?: return "All devices"
        return when {
            devices.size == 1 && devices[0].equals(self, ignoreCase = true) -> "This phone only"
            devices.size == 1 -> "1 device"
            else -> "${devices.size} devices"
        }
    }

    /** The task's number as it is shown and copied ("143"); null before it has one. */
    fun id(task: TaskItem): String? = task.number?.value?.toString()
}
