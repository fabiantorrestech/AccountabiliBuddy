package com.accountabilibuddy.companion.stats

import java.time.DayOfWeek
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale

/** Dates, ranges and short texts the Stats builders share (the PC's StatsFormat). Times are 12-hour whatever the phone's setting. */
object StatsFormat {
    private val locale: Locale get() = Locale.getDefault()

    /** "2h 15m", "40m", "1h": the PC's GoalProgress.Duration (minutes cut, not rounded). */
    fun duration(time: Duration): String {
        val hours = time.toHours().toInt()
        val minutes = (time.toMinutes() % 60).toInt()
        return if (hours == 0) "${minutes}m" else if (minutes == 0) "${hours}h" else "${hours}h ${minutes}m"
    }

    /** "7h 05m" for the numerals; minutes always two digits. */
    fun hoursMinutes(time: Duration): String {
        val hours = time.toHours().toInt()
        val minutes = (time.toMinutes() % 60).toInt()
        return if (hours == 0) "${minutes}m" else "${hours}h %02dm".format(minutes)
    }

    /** [time] in minutes, with the seconds as a fraction (the PC's TotalMinutes). */
    fun minutes(time: Duration): Double = time.toNanos() / 60_000_000_000.0

    /** The Monday of the week [date] is in. */
    fun mondayOf(date: LocalDate): LocalDate = date.minusDays((date.dayOfWeek.value - 1).toLong())

    /** The first and last date the range covers: this week, this month, the last 365 days, or just today. */
    fun bounds(range: StatsRange, today: LocalDate): Pair<LocalDate, LocalDate> = when (range) {
        StatsRange.Week -> mondayOf(today).let { it to it.plusDays(6) }
        StatsRange.Month -> YearMonth.from(today).let { it.atDay(1) to it.atEndOfMonth() }
        StatsRange.Year -> today.minusDays(364) to today
        StatsRange.Day -> today to today
    }

    /** "Oct 26 – Nov 1", or one date as "Mon, Oct 26". */
    fun datesText(from: LocalDate, to: LocalDate): String {
        val short = DateTimeFormatter.ofPattern("MMM d", locale)
        return when {
            from == to -> dayText(from)
            from.year == to.year -> "${from.format(short)} – ${to.format(short)}"
            else -> DateTimeFormatter.ofPattern("MMM d, yyyy", locale).let { "${from.format(it)} – ${to.format(it)}" }
        }
    }

    /** "Mon, Oct 26". */
    fun dayText(date: LocalDate): String = date.format(DateTimeFormatter.ofPattern("EEE, MMM d", locale))

    /** "Oct 26". */
    fun shortDate(date: LocalDate): String = date.format(DateTimeFormatter.ofPattern("MMM d", locale))

    /** A weekday's first letter, for the labels under the week's bars. */
    fun weekdayLetter(date: LocalDate): String = date.dayOfWeek.getDisplayName(TextStyle.SHORT, locale).take(1).uppercase(locale)

    /** The label under a day's bar: the weekday's letter for a week, the date for some days of a month (the bars are too narrow for all). */
    fun barLabel(range: StatsRange, date: LocalDate): String = when {
        range == StatsRange.Week -> weekdayLetter(date)
        date.dayOfMonth in setOf(1, 8, 15, 22, 29) -> date.dayOfMonth.toString()
        else -> ""
    }

    /** "Mon" .. "Sun" for a row 0 (Monday) to 6 (Sunday). */
    fun weekdayShort(row: Int): String = DayOfWeek.of(row + 1).getDisplayName(TextStyle.SHORT, locale)

    fun weekdayName(day: DayOfWeek): String = day.getDisplayName(TextStyle.FULL, locale)

    /** "9 AM", "12 PM" for an hour 0 to 24 (24 is the next midnight). */
    fun hour(hour: Int): String = "${if (hour % 12 == 0) 12 else hour % 12} ${if (hour % 24 < 12) "AM" else "PM"}"

    /** "9–11 AM", or "11 AM–1 PM" when the two ends are on different sides of noon. */
    fun hourRange(start: Int, end: Int): String {
        val sameHalf = (start % 24 < 12) == (end % 24 < 12)
        val from = if (sameHalf) (if (start % 12 == 0) 12 else start % 12).toString() else hour(start)
        return "$from–${hour(end)}"
    }

    /** "6a", "12p": a column label of the Intensity grid. */
    fun hourShort(hour: Int): String = "${if (hour % 12 == 0) 12 else hour % 12}${if (hour < 12) "a" else "p"}"

    /** "4:05 PM". */
    fun time(at: LocalDateTime): String = at.format(DateTimeFormatter.ofPattern("h:mm a", locale))

    /** "7:05 AM" for a time of day given as minutes after midnight. */
    fun timeOfDay(minutes: Int): String = LocalDateTime.of(2000, 1, 1, minutes / 60 % 24, minutes % 60).let(::time)

    fun plural(count: Int, noun: String, many: String = noun + "s"): String = if (count == 1) noun else many
}
