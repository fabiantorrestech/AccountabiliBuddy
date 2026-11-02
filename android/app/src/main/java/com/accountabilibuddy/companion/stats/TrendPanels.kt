package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.temporal.IsoFields
import kotlin.math.abs

/**
 * The Focus graph (board 19g): a daily average of deep work and drift per week over the last 12 weeks (Month), or day by day
 * over the last two weeks (Week). Null when there is no deep work in it, or no PC has sent its daily goal (the goal line and
 * the unfinished-today rule need it).
 */
object TrendPanels {
    private const val WEEKS = 12
    private const val DAILY_DAYS = 14
    private const val GOAL_HEADROOM = 1.25
    private val STEADY_BAND: Duration = Duration.ofMinutes(15)

    fun build(data: StatsData, range: StatsRange): TrendPanel? {
        val goal = data.goal ?: return null
        return if (range == StatsRange.Week) daily(data, goal) else weekly(data, goal)
    }

    private fun weekly(data: StatsData, goal: Duration): TrendPanel? {
        val driftPerDay = data.driftPerDay()
        val trend = FocusTrend.build(data.historyDays, driftPerDay, data.isWorkday, data.today, goal, WEEKS)
        if (!trend.hasData) return null

        val main = trend.weeks.map { week -> week.dailyFocus?.let { StatsFormat.minutes(it) } }
        val drift = trend.weeks.map { week -> week.dailyDrift?.let { StatsFormat.minutes(it) } }
        // The newest week is labelled, and every second one before it.
        val labels = trend.weeks.mapIndexed { index, week ->
            if ((trend.weeks.size - 1 - index) % 2 == 0) "W${week.start.get(IsoFields.WEEK_OF_WEEK_BASED_YEAR)}" else null
        }
        val descriptions = trend.weeks.map { week ->
            val start = StatsFormat.shortDate(week.start)
            week.dailyFocus?.let { "Week of $start · ${StatsFormat.duration(it)} a day · drift ${StatsFormat.duration(week.dailyDrift ?: Duration.ZERO)} a day" } ?: "Week of $start · no data"
        }

        val cards = mutableListOf<StatCard>()
        val latest = trend.latest
        val latestFocus = latest?.dailyFocus
        if (latest != null && latestFocus != null) {
            val before = trend.previous?.dailyFocus
            val detail = when {
                before == null -> "a day so far this week"
                latestFocus.minus(before).abs() < Duration.ofMinutes(1) -> "a day · same as last week"
                latestFocus > before -> "a day · up ${StatsFormat.duration(latestFocus.minus(before))}"
                else -> "a day · down ${StatsFormat.duration(before.minus(latestFocus))}"
            }
            val title = if (latest.start == StatsFormat.mondayOf(data.today)) "This week" else "Last week"
            cards += StatCard(title, StatsFormat.hoursMinutes(latestFocus), detail)
        }

        trend.change?.let { change ->
            val sign = if (change.isNegative) "−" else "+"
            val word = if (change > STEADY_BAND) "steady climb" else if (change < STEADY_BAND.negated()) "slipping" else "holding steady"
            cards += StatCard("${trend.trendCount}-week trend", "$sign${"%.1f".format(abs(change.toNanos() / 3_600_000_000_000.0))}h", word)
        }

        trend.driftAverage?.let { cards += StatCard("Drift", StatsFormat.duration(it), "a day · average over ${trend.trendCount} weeks") }
        return panel(goal, "Daily average · $WEEKS weeks", main, drift, labels, descriptions, cards, StatsDepth.caption(data, WEEKS * 7))
    }

    private fun daily(data: StatsData, goal: Duration): TrendPanel? {
        val driftPerDay = data.driftPerDay()
        val days = data.historyDays.takeLast(DAILY_DAYS)
        if (days.all { it.focused <= Duration.ZERO }) return null

        // Off days with no focus are gaps, and today isn't counted until its goal is met (as in Month and Consistency).
        fun counts(day: StatsDay) = (data.isWorkday(day.date) || day.focused > Duration.ZERO) &&
            !(day.date == data.today && data.isWorkday(day.date) && day.focused < goal)
        val counted = days.filter(::counts)
        if (counted.isEmpty()) return null

        val main = days.map { if (counts(it)) StatsFormat.minutes(it.focused) else null }
        val drift = days.map { if (counts(it)) StatsFormat.minutes(driftPerDay[it.date] ?: Duration.ZERO) else null }
        val labels = days.map { StatsFormat.weekdayLetter(it.date) }
        val descriptions = days.map {
            if (counts(it)) "${StatsFormat.dayText(it.date)} · ${StatsFormat.duration(it.focused)} · drift ${StatsFormat.duration(driftPerDay[it.date] ?: Duration.ZERO)}"
            else "${StatsFormat.dayText(it.date)} · not counted"
        }

        val average = Duration.ofNanos(counted.map { it.focused.toNanos() }.average().toLong())
        val best = counted.maxByOrNull { it.focused }!!
        val cards = mutableListOf(
            StatCard("Average", StatsFormat.hoursMinutes(average), "a day · over ${counted.size} counted days"),
            StatCard("Best day", StatsFormat.hoursMinutes(best.focused), StatsFormat.dayText(best.date)),
        )
        if (driftPerDay.isNotEmpty()) {
            val driftAverage = Duration.ofNanos(counted.map { (driftPerDay[it.date] ?: Duration.ZERO).toNanos() }.average().toLong())
            cards += StatCard("Drift", StatsFormat.duration(driftAverage), "a day · average")
        }

        return panel(goal, "Day by day · last $DAILY_DAYS days", main, drift, labels, descriptions, cards, StatsDepth.caption(data, DAILY_DAYS))
    }

    private fun panel(
        goal: Duration, subtitle: String, main: List<Double?>, drift: List<Double?>, labels: List<String?>, descriptions: List<String>,
        cards: List<StatCard>, caption: String?,
    ): TrendPanel {
        val goalMinutes = StatsFormat.minutes(goal)
        val maximum = maxOf(goalMinutes * GOAL_HEADROOM, main.filterNotNull().maxOrNull() ?: 0.0)
        return TrendPanel(subtitle, main, drift, labels, descriptions, maximum, goalMinutes, "goal ${StatsFormat.duration(goal)} / day", cards, caption)
    }
}
