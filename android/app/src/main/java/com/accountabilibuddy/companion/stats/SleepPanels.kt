package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.engine.NightKind
import com.accountabilibuddy.companion.engine.NightRecord
import com.accountabilibuddy.companion.engine.SmartBedtime
import java.time.Duration
import java.time.LocalDate

/**
 * The Sleep view's numbers, from the nights this phone measured (Health Connect, read on the phone) and the smart bedtime's
 * summary. Local to the phone: nothing here is sent anywhere. A night with no measurement has no bar, never a zero.
 */
object SleepPanels {
    private const val NIGHTS = 14
    private const val GOAL_HEADROOM = 1.15

    /** The Overview tile: the average night over the nights measured; null with none. */
    fun card(nights: List<NightRecord>, today: LocalDate): StatCard? {
        val recent = recent(nights, today)
        if (recent.isEmpty()) return null
        val summaries = NightKind.entries.mapNotNull { kind -> SmartBedtime.summary(nights, kind, today).takeIf { it.askedThisWeek > 0 } }
        val made = summaries.sumOf { it.madeThisWeek }
        val asked = summaries.sumOf { it.askedThisWeek }
        val detail = if (asked > 0) "bedtime kept $made of $asked this week" else "${recent.size} ${StatsFormat.plural(recent.size, "night")} measured"
        return StatCard("Sleep", StatsFormat.hoursMinutes(average(recent)), "avg · $detail", StatsView.Sleep)
    }

    /** @param goal The sleep goal (Settings › Sleep). Null when no night was measured in the last 14 days. */
    fun build(nights: List<NightRecord>, goal: Duration, today: LocalDate): BarPanel? {
        val recent = recent(nights, today)
        if (recent.isEmpty()) return null

        val byEvening = recent.associateBy { it.evening }
        val scale = maxOf(StatsFormat.minutes(goal) * GOAL_HEADROOM, recent.maxOf { it.sleptMinutes }.toDouble(), 1.0)
        val bars = (NIGHTS downTo 1).map { back ->
            val evening = today.minusDays(back.toLong())
            val morning = evening.plusDays(1)
            val night = byEvening[evening]
            StatsBar(
                StatsFormat.weekdayLetter(morning), ((night?.sleptMinutes ?: 0) / scale).toFloat(), BarKind.Sleep, morning == today, night == null,
                if (night == null) "Night into ${StatsFormat.dayText(morning)} · not measured"
                else "Night into ${StatsFormat.dayText(morning)} · ${StatsFormat.duration(Duration.ofMinutes(night.sleptMinutes.toLong()))}",
                null,
            )
        }

        val cards = mutableListOf<StatCard>()
        for ((kind, title) in listOf(NightKind.Workday to "Workday nights", NightKind.OffDay to "Other nights")) {
            val summary = SmartBedtime.summary(nights, kind, today)
            val slept = summary.averageSleptMinutes
            if (slept == null || summary.nights <= 0) continue
            val asleep = summary.averageAsleep?.let { "asleep around ${StatsFormat.timeOfDay(it.hour * 60 + it.minute)} · " } ?: ""
            cards += StatCard(title, StatsFormat.hoursMinutes(Duration.ofMinutes(slept.toLong())), "$asleep${summary.nights} ${StatsFormat.plural(summary.nights, "night")}")
        }

        val kept = NightKind.entries.map { SmartBedtime.summary(nights, it, today) }
        val asked = kept.sumOf { it.askedThisWeek }
        if (asked > 0) cards += StatCard("Made it this week", "${kept.sumOf { it.madeThisWeek }} of $asked", "nights you kept the bedtime asked")
        return BarPanel(
            StatsFormat.hoursMinutes(average(recent)), "average sleep · ${recent.size} ${StatsFormat.plural(recent.size, "night")} measured by your watch or phone",
            bars, (StatsFormat.minutes(goal) / scale).toFloat(), "goal ${StatsFormat.duration(goal)}", cards, emptyList(), caption(recent.size),
        )
    }

    private fun recent(nights: List<NightRecord>, today: LocalDate): List<NightRecord> =
        nights.filter { !it.evening.isBefore(today.minusDays(NIGHTS.toLong())) && it.evening.isBefore(today) }

    private fun average(nights: List<NightRecord>): Duration = Duration.ofMinutes(Math.rint(nights.sumOf { it.sleptMinutes }.toDouble() / nights.size).toLong())

    // Health Connect is read for the last 21 days, so that is as far back as this view can look.
    private fun caption(measured: Int): String? =
        if (measured < NIGHTS) "Measured sleep is read from Health Connect for the last 21 days: $measured of the last $NIGHTS nights have a measurement." else null
}
