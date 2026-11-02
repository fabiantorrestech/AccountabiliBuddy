package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.TaskPlannedFor
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** How the Tasks pane words times, estimates and the muted line under a task; the same wording as the PC's Tasks page, always 12-hour. */
internal object TaskText {
    private val clock = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val dayAndDate = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)
    private val weekday = DateTimeFormatter.ofPattern("EEE", Locale.US)
    private val monthDay = DateTimeFormatter.ofPattern("MMM d", Locale.US)

    /** "11:02 AM". */
    fun clock(time: Instant, zone: ZoneId = ZoneId.systemDefault()): String = clock.format(time.atZone(zone))

    /** "11:02 AM" today, else "Thu 10/23 11:02 AM". */
    fun whenText(time: Instant, today: LocalDate, zone: ZoneId = ZoneId.systemDefault()): String {
        val local = time.atZone(zone)
        return if (local.toLocalDate() == today) clock.format(local) else "${dayAndDate.format(local)} ${clock.format(local)}"
    }

    /** "today", "tomorrow", "yesterday", "Fri" within the next six days, else "Nov 3". */
    fun dayLabel(date: LocalDate, today: LocalDate): String {
        val days = date.toEpochDay() - today.toEpochDay()
        return when {
            days == 0L -> "today"
            days == 1L -> "tomorrow"
            days == -1L -> "yesterday"
            days in 2..6 -> weekday.format(date)
            else -> monthDay.format(date)
        }
    }

    /** 25 -> "25 min", 90 -> "90 min", 135 -> "2h 15m", 120 -> "2h". */
    fun estimate(minutes: Int): String {
        if (minutes < 100) return "$minutes min"
        val rest = minutes % 60
        return if (rest == 0) "${minutes / 60}h" else "${minutes / 60}h ${rest.toString().padStart(2, '0')}m"
    }

    /** "2 of 3 steps left" for a task with steps; null with none. */
    fun stepsLeft(steps: List<SessionTaskItem>?): String? {
        if (steps.isNullOrEmpty()) return null
        return "${steps.count { !it.isDone }} of ${steps.size} steps left"
    }

    /** "planned Fri 3:00 PM" from the plan part the PC wrote; null when it can't be read. */
    fun planned(slot: TaskPlannedFor?, today: LocalDate): String? {
        slot ?: return null
        val date = runCatching { LocalDate.parse(slot.date) }.getOrNull() ?: return null
        val start = runCatching { LocalTime.parse(slot.start) }.getOrNull() ?: return null
        return "planned ${dayLabel(date, today)} ${clock.format(start)}"
    }

    /** The parts that exist, joined with " · ". */
    fun line(vararg parts: String?): String = parts.filterNotNull().filter { it.isNotEmpty() }.joinToString(" · ")
}
