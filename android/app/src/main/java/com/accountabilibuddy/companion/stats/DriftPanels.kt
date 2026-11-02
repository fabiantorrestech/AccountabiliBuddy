package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDateTime

/**
 * The Drift view: time off track inside the user's counted sessions, per day, with the worst day and the busiest hour. It reads
 * times only (ADR 8: drift never names a site or app here). Null when no device has sent a session.
 */
object DriftPanels {
    fun build(data: StatsData, range: StatsRange): BarPanel? {
        val (from, to) = StatsFormat.bounds(range, data.today)
        val past = data.daysBetween(from, to)
        val visits = data.driftBetween(from, to)
        if (past.all { it.focused <= Duration.ZERO } && visits.isEmpty()) return null

        val perDay = visits.groupBy { data.dateOf(it.start) }.mapValues { (_, list) -> list.fold(Duration.ZERO) { sum, visit -> sum.plus(visit.length) } }
        val scale = maxOf(30.0, perDay.values.maxOfOrNull { StatsFormat.minutes(it) } ?: 0.0)
        val bars = mutableListOf<StatsBar>()
        var date = from
        while (!date.isAfter(to)) {
            val noBar = date.isAfter(data.today) || data.dayOf(date) == null
            val drift = perDay[date] ?: Duration.ZERO
            val label = StatsFormat.barLabel(range, date)
            bars += StatsBar(
                label, (StatsFormat.minutes(drift) / scale).toFloat(), BarKind.Drift, date == data.today, noBar,
                if (noBar) "${StatsFormat.dayText(date)} · no data" else "${StatsFormat.dayText(date)} · ${StatsFormat.duration(drift)} off track", date.takeUnless { noBar },
            )
            date = date.plusDays(1)
        }

        val total = visits.fold(Duration.ZERO) { sum, visit -> sum.plus(visit.length) }
        val cards = mutableListOf<StatCard>()
        if (visits.isNotEmpty()) {
            val worst = perDay.maxByOrNull { it.value }!!
            cards += StatCard("Worst day", StatsFormat.shortDate(worst.key), StatsFormat.duration(worst.value))
            val hour = visits.groupBy { LocalDateTime.ofInstant(it.start, data.zone).hour }.map { (hour, list) -> hour to list.size }.maxByOrNull { it.second }!!
            cards += StatCard("Busiest hour", StatsFormat.hourRange(hour.first, hour.first + 1), "${hour.second} ${StatsFormat.plural(hour.second, "stretch", "stretches")} began then")
        }

        val note = if (visits.isEmpty()) "no drift in your sessions" else "stretches off track · ${StatsFormat.duration(total)} in sessions"
        return BarPanel(visits.size.toString(), note, bars, null, null, cards, emptyList(), StatsDepth.caption(data, if (range == StatsRange.Week) 7 else 28))
    }

}
