package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * The Phone view's numbers: this phone's own time and pickups for the last days Android still has records of (about a week).
 * Local to the phone; only the pickups inside the PC's sessions travel, as they already did (ADR 26). The oldest days it holds
 * are shown, never padded with zeros.
 */
object PhonePanels {
    /** How many days the view shows. */
    const val DAYS = 7

    /** The Overview tile: the usual day's time over the days before today (today alone when it is the only one); null with none. */
    fun card(days: List<PhoneDay>, today: LocalDate): StatCard? {
        val basis = usual(days, today) ?: return null
        return StatCard("Phone", StatsFormat.hoursMinutes(average(basis)), "a day on average · last ${basis.size} ${StatsFormat.plural(basis.size, "day")}", StatsView.Phone)
    }

    fun build(days: List<PhoneDay>, today: LocalDate): BarPanel? {
        val basis = usual(days, today) ?: return null
        val scale = maxOf(days.maxOf { StatsFormat.minutes(it.total) }, 30.0)
        val byDate = days.associateBy { it.date }
        val bars = (DAYS - 1 downTo 0).map { back ->
            val date = today.minusDays(back.toLong())
            val day = byDate[date]
            StatsBar(
                StatsFormat.weekdayLetter(date), ((day?.let { StatsFormat.minutes(it.total) } ?: 0.0) / scale).toFloat(),
                if (date == today) BarKind.Accent else BarKind.Plan, date == today, day == null,
                if (day == null) "${StatsFormat.dayText(date)} · no record" else "${StatsFormat.dayText(date)} · ${StatsFormat.duration(day.total)} on the phone",
                null,
            )
        }

        val focusTotal = days.fold(Duration.ZERO) { sum, day -> sum.plus(day.inFocus) }
        val cards = mutableListOf(
            StatCard("During focus", StatsFormat.hoursMinutes(focusTotal), "on the phone in the last ${days.size} ${StatsFormat.plural(days.size, "day")}"),
        )
        val pickups = days.mapNotNull { it.pickups }
        if (pickups.isNotEmpty()) {
            val usualPickups = basis.mapNotNull { it.pickups }.ifEmpty { pickups }
            cards += StatCard("Pickups", Math.rint(usualPickups.average()).toInt().toString(), "a day on average")
        }

        return BarPanel(
            StatsFormat.hoursMinutes(average(basis)), "on the phone a day, last ${basis.size} ${StatsFormat.plural(basis.size, "day")}", bars, null, null, cards, emptyList(),
            "Android keeps about a week of phone use, so that is as far back as this goes.",
        )
    }

    private fun usual(days: List<PhoneDay>, today: LocalDate): List<PhoneDay>? {
        if (days.isEmpty() || days.all { it.total < Duration.ofMinutes(1) }) return null
        val past = days.filter { it.date.isBefore(today) }
        return past.ifEmpty { days }
    }

    private fun average(days: List<PhoneDay>): Duration =
        Duration.ofSeconds(Math.rint(days.sumOf { it.total.seconds }.toDouble() / days.size).toLong())
}
