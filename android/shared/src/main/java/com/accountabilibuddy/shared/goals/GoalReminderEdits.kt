package com.accountabilibuddy.shared.goals

import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/**
 * Edit goal › Reminders on the phone (round 2 R6b3; the PC's `GoalReminderEditRow` and `GoalRemindersEditorViewModel`, board
 * Goals-PC-Edit-Reminders): the day choices (Today, Every day, Every Sunday … Saturday, On a date, or a stored set of weekdays), the
 * day and time pills, the repeat, the lines a row shows, the goal's rule switches applied to all its reminders, and "Next: today
 * 8:00 PM". Times are always 12-hour. Reasons are private text: shown, never logged.
 */
object GoalReminderEdits {
    const val TODAY = 0
    const val EVERY_DAY = 1

    /** Every Sunday is 2, … Every Saturday 8. */
    const val FIRST_WEEKDAY = 2
    const val ON_A_DATE = 9

    /** A stored reminder on several weekdays keeps them as a choice of its own. */
    const val SEVERAL_WEEKDAYS = 10

    /** A new reminder's time (the PC's "Add a reminder": every day at 8:00 PM). */
    val DEFAULT_TIME: LocalTime = LocalTime.of(20, 0)

    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /** The day menu's labels; [SEVERAL_WEEKDAYS] only for a reminder that has them. */
    fun dayChoices(reminder: GoalReminder): List<String> {
        val base = listOf("Today", "Every day") + GoalWeekday.entries.map { "Every ${it.name}" } + "On a date"
        return if (several(reminder) != null) base + weekdaysText(several(reminder)!!) else base
    }

    fun dayChoice(reminder: GoalReminder): Int = when (reminder.days) {
        GoalReminderDays.Today -> TODAY
        GoalReminderDays.EveryDay -> EVERY_DAY
        GoalReminderDays.Weekdays -> if (several(reminder) != null) SEVERAL_WEEKDAYS else FIRST_WEEKDAY + (reminder.weekdays?.firstOrNull() ?: GoalWeekday.Sunday).ordinal
        GoalReminderDays.Date -> ON_A_DATE
    }

    /**
     * [reminder] with the day choice picked again (milestone review S1: only a pick sets "Today" to [today]; a stored Today reminder
     * keeps its day otherwise). [date] is the day for [ON_A_DATE] (today when none picked).
     */
    fun withDayChoice(reminder: GoalReminder, choice: Int, today: LocalDate, date: LocalDate? = null): GoalReminder = when (choice) {
        TODAY -> reminder.copy(days = GoalReminderDays.Today, weekdays = null, date = today, repeat = false)
        EVERY_DAY -> reminder.copy(days = GoalReminderDays.EveryDay, weekdays = null, date = null, repeat = true)
        ON_A_DATE -> reminder.copy(days = GoalReminderDays.Date, weekdays = null, date = date ?: reminder.date ?: today, repeat = false)
        SEVERAL_WEEKDAYS -> reminder
        else -> reminder.copy(days = GoalReminderDays.Weekdays, weekdays = listOf(GoalWeekday.entries[choice - FIRST_WEEKDAY]), date = null, repeat = true)
    }

    /** "Today", "Every day", "Sun", "Fri 12/5", "Mon, Wed" (a Today reminder from another day shows its date). */
    fun dayPill(reminder: GoalReminder, today: LocalDate): String = when (reminder.days) {
        GoalReminderDays.Today -> reminder.date?.takeIf { it != today }?.let { GoalTexts.date(it, today) } ?: "Today"
        GoalReminderDays.EveryDay -> "Every day"
        GoalReminderDays.Date -> reminder.date?.let { GoalTexts.date(it, today) } ?: "A date"
        GoalReminderDays.Weekdays -> several(reminder)?.let(::weekdaysText) ?: (reminder.weekdays?.firstOrNull() ?: GoalWeekday.Sunday).name.take(3)
    }

    /** "8:00 PM". */
    fun timePill(time: LocalTime): String = time.format(CLOCK)

    /** "Every day", "Every week", "Once". */
    fun repeatText(reminder: GoalReminder): String = when (reminder.days) {
        GoalReminderDays.Today, GoalReminderDays.Date -> "Once"
        GoalReminderDays.EveryDay -> "Every day"
        GoalReminderDays.Weekdays -> if (reminder.repeat) "Every week" else "Once"
    }

    /** A row's title (board Goals-Phone-New-2): "Today 8:00 PM". */
    fun title(reminder: GoalReminder, today: LocalDate): String = "${dayPill(reminder, today)} ${timePill(reminder.time)}"

    /** A row's subtitle: "If not done yet · every day" (the reason, then the repeat), or just "Every day". */
    fun subtitle(reminder: GoalReminder): String {
        val repeat = repeatText(reminder)
        val reason = reminder.reason?.trim().orEmpty()
        return if (reason.isEmpty()) repeat else "${reason.replaceFirstChar { it.uppercase() }} · ${repeat.lowercase()}"
    }

    /** A new reminder for [goalId]: every day at 8:00 PM, with the goal's rule switches. Stamped when saved. */
    fun newReminder(goalId: UUID, position: Double, skipWhenDone: Boolean, nudgeWhenBehind: Boolean, now: OffsetDateTime): GoalReminder =
        GoalReminder(
            UUID.randomUUID(), goalId, GoalReminderDays.EveryDay, DEFAULT_TIME, position, now,
            skipWhenDone = skipWhenDone, nudgeWhenBehind = nudgeWhenBehind,
        )

    /** The goal's rule switches on every one of its reminders, numbered 1..N in their order. */
    fun withRules(reminders: List<GoalReminder>, skipWhenDone: Boolean, nudgeWhenBehind: Boolean): List<GoalReminder> =
        reminders.mapIndexed { index, reminder ->
            reminder.copy(position = index + 1.0, skipWhenDone = skipWhenDone, nudgeWhenBehind = nudgeWhenBehind, reason = reminder.reason?.trim()?.ifEmpty { null })
        }

    /**
     * The rule switches the goal's Reminders page starts with: its first reminder's, else Settings › Goals' defaults (the nudge per mode,
     * brief §5.10).
     */
    fun rulesOf(reminders: List<GoalReminder>, defaults: GoalReminderDefaults, productaBuddy: Boolean): Pair<Boolean, Boolean> {
        val first = reminders.firstOrNull { it.isLive }
        return if (first != null) first.skipWhenDone to reminders.any { it.isLive && it.nudgeWhenBehind } else defaults.skipWhenDone to defaults.nudgeFor(productaBuddy)
    }

    /**
     * The next moment one of [reminders] rings for [goal] after [now] within two weeks; null when none (or the goal is paused today).
     * As it would be scheduled: a moment already handled today isn't skipped here (the line is a plan, not a log).
     */
    fun nextAt(reminders: List<GoalReminder>, goal: Goal, now: Instant, zone: ZoneId): java.time.OffsetDateTime? {
        val today = now.atZone(zone).toLocalDate()
        if (goal.isPausedOn(today)) return null
        val live = reminders.filter { it.isLive && it.goalId == goal.id }
        for (offset in 0L..14L) {
            val day = today.plusDays(offset)
            if (goal.isPausedOn(day)) continue
            val next = live.mapNotNull { GoalReminderTimes.on(it, day, zone) }.filter { it.toInstant() > now }.minByOrNull { it.toInstant() }
            if (next != null) return next
        }
        return null
    }

    /** "Next: today 8:00 PM", "Next: tomorrow 9:00 AM", "Next: Fri 8:00 PM", "Next: Fri 12/5 8:00 PM"; null when none. */
    fun nextText(reminders: List<GoalReminder>, goal: Goal, now: Instant, zone: ZoneId): String? {
        val next = nextAt(reminders, goal, now, zone)?.atZoneSameInstant(zone) ?: return null
        val today = now.atZone(zone).toLocalDate()
        val day = next.toLocalDate()
        val dayText = when {
            day == today -> "today"
            day == today.plusDays(1) -> "tomorrow"
            day < today.plusDays(7) -> GoalTexts.weekday(day)
            else -> GoalTexts.date(day, today)
        }
        return "Next: $dayText ${next.format(CLOCK)}"
    }

    private fun several(reminder: GoalReminder): List<GoalWeekday>? =
        reminder.weekdays?.takeIf { reminder.days == GoalReminderDays.Weekdays && it.size > 1 }

    private fun weekdaysText(days: List<GoalWeekday>): String = days.sortedBy { it.ordinal }.joinToString(", ") { it.name.take(3) }
}
