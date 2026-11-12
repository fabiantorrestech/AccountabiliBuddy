package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * One goal over a range of days, for the phone's Stats (round 2 R6b4): a bar per day, where the target line sits, and the range's
 * numbers. Phone only (the PC's Stats draws the goal's year as its heatmap); every day comes from the goals' own maths
 * ([GoalCounting]) and is marked as the goal heatmap marks it ([GoalStrip14.cellOf]), so a day reads the same in both places.
 *
 * Paused days are hatched: neither hit nor missed (brief §5.4). They are left out of every number here (days hit, total, average),
 * whatever was measured on them, as the PC leaves them out of streaks, the week's counts and the goal's age.
 *
 * @property days Every day from [from] to [to], oldest first (days still to come included, drawn as nothing).
 * @property targetFraction Where the dashed target line sits, 0 to 1 of the chart's height; null when the goal asks nothing of a day.
 * @property targetLabel "target 15 min", "at most 2h", "pace 26 min a day" (a weekly or monthly amount spread over its days).
 * @property big The range's headline: "12 of 14" for a goal that counts days, a total ("5h 20m"), or a level goal's latest value.
 * @property note What [big] counts ("days hit · 3 paused days left out").
 * @property askedDays Days that were hit, got something or were missed (paused, not due and coming days left out).
 * @property hitDays Days that met their own target.
 * @property pausedDays Paused days in the range, up to today.
 * @property total The sum of the days' amounts; null for a level goal and a habit ticked by hand (nothing to add up).
 * @property average The amount a day over the asked days before today (today is a part day); null with none.
 */
data class GoalStatsSeries(
    val from: LocalDate,
    val to: LocalDate,
    val days: List<GoalStatsDay>,
    val targetFraction: Double?,
    val targetLabel: String?,
    val big: String,
    val note: String,
    val tiles: List<GoalStatsTile>,
    val askedDays: Int,
    val hitDays: Int,
    val pausedDays: Int,
    val total: Double?,
    val average: Double?,
) {
    companion object {
        /** The room kept above the target line, so a day at its target doesn't touch the top. */
        const val TARGET_HEADROOM = 1.25

        // A level goal's chart starts a little under its lowest value (a weight's bars from zero would all look the same).
        private const val LEVEL_MARGIN = 0.15

        fun build(goal: Goal, data: GoalSourceData, from: LocalDate, to: LocalDate, today: LocalDate): GoalStatsSeries {
            val last = if (to < today) to else today
            val states = if (from > last) emptyList() else GoalCounting.days(goal, data, from, last, today)
            val isLevel = goal.target.direction == GoalDirection.Reach
            val threshold = if (goal.kind == GoalKind.Habit) GoalTexts.threshold(goal) else null
            val ticksOnly = goal.kind == GoalKind.Habit && threshold == null

            // Each day's mark and amount; a paused day carries nothing into the numbers.
            val marks = states.map { markOf(GoalStrip14.cellOf(goal, it, today).kind) }
            val amounts = states.mapIndexed { index, state -> if (marks[index] == GoalStatsDayMark.Paused) 0.0 else amountOf(goal, state, threshold) }

            val dayTarget = dayTarget(goal, threshold, last)
            val drawn = states.indices.filter { marks[it] != GoalStatsDayMark.Paused && marks[it] != GoalStatsDayMark.None && (states[it].hasValue || !isLevel) }.toSet()
            val values = drawn.map { amounts[it] }
            val (floor, top) = scale(values, dayTarget, isLevel, ticksOnly)
            fun fractionOf(amount: Double): Double = if (top <= floor) 0.0 else ((amount - floor) / (top - floor)).coerceIn(0.0, 1.0)

            val days = mutableListOf<GoalStatsDay>()
            states.forEachIndexed { index, state ->
                val shown = index in drawn
                days += GoalStatsDay(state.day, amounts[index], if (shown) fractionOf(amounts[index]) else 0.0, marks[index], GoalHeatmap.dayTip(goal, state, today))
            }

            var coming = last.plusDays(1)
            if (coming < from) {
                coming = from
            }

            while (coming <= to) {
                days += GoalStatsDay(coming, 0.0, 0.0, GoalStatsDayMark.None, "${GoalTexts.date(coming, today)} · still to come")
                coming = coming.plusDays(1)
            }

            val asked = states.indices.filter { marks[it] == GoalStatsDayMark.Hit || marks[it] == GoalStatsDayMark.Some || marks[it] == GoalStatsDayMark.Missed }
            val hit = marks.count { it == GoalStatsDayMark.Hit }
            val paused = marks.count { it == GoalStatsDayMark.Paused }
            val additive = !isLevel && !ticksOnly
            val total = if (additive) states.indices.filter { marks[it] != GoalStatsDayMark.Paused }.sumOf { amounts[it] } else null

            // The average: whole days only (today is a part day). A level goal averages the days that have a value.
            val averaged = asked.filter { states[it].day != today && (!isLevel || states[it].hasValue) }
            val average = if (ticksOnly || averaged.isEmpty()) null else averaged.sumOf { amounts[it] } / averaged.size

            fun text(amount: Double): String = if (goal.kind == GoalKind.Habit) GoalTexts.minutes(amount) else GoalTexts.value(amount, goal.target.unit)
            val pausedNote = if (paused > 0) " · ${GoalTexts.plural(paused, "paused day")} left out" else ""
            val countsDays = GoalCounting.countsDays(goal)
            val latest = states.indices.lastOrNull { marks[it] != GoalStatsDayMark.Paused && states[it].hasValue }
            val (big, note) = when {
                isLevel -> (latest?.let { text(amounts[it]) } ?: "–") to (if (latest == null) "no value in this range" else "latest value · ${GoalTexts.date(states[latest].day, today)}")
                countsDays -> "$hit of ${asked.size}" to "days hit$pausedNote"
                else -> text(total ?: 0.0) to "counted in this range$pausedNote"
            }

            val tiles = mutableListOf<GoalStatsTile>()
            if (countsDays && !isLevel) {
                tiles += GoalStatsTile("Days hit", "$hit of ${asked.size}", if (paused > 0) "paused days left out" else "days that asked for it")
            }

            if (total != null) {
                tiles += GoalStatsTile("Total", text(total), "in this range")
            }

            if (average != null) {
                tiles += GoalStatsTile("Average", text(average), if (isLevel) "over days with a value" else "a day · today left out")
            }

            if (paused > 0) {
                tiles += GoalStatsTile("Paused", GoalTexts.plural(paused, "day"), "hatched · neither hit nor missed")
            }

            return GoalStatsSeries(
                from, to, days,
                dayTarget?.let { fractionOf(it) },
                dayTarget?.let { targetLabel(goal, it, threshold) },
                big, note, tiles, asked.size, hit, paused, total, average,
            )
        }

        private fun markOf(kind: GoalStripCellKind): GoalStatsDayMark = when (kind) {
            GoalStripCellKind.Hit -> GoalStatsDayMark.Hit
            GoalStripCellKind.Partial -> GoalStatsDayMark.Some
            GoalStripCellKind.Missed -> GoalStatsDayMark.Missed
            GoalStripCellKind.Paused -> GoalStatsDayMark.Paused
            GoalStripCellKind.Today -> GoalStatsDayMark.Today
            GoalStripCellKind.NotDue -> GoalStatsDayMark.None
        }

        // A habit with a "ticks itself at" time shows its minutes; one ticked by hand, its tick; every other goal the day's value.
        private fun amountOf(goal: Goal, state: GoalDayState, threshold: Double?): Double =
            if (goal.kind == GoalKind.Habit && threshold != null) state.minutes else state.value

        /**
         * What one day is measured against (the heatmap's rule, [GoalHeat]): a habit's threshold, the daily amount (or the amount for
         * each of "N days"), a level goal's level, else the period's amount spread over its days. Null when nothing is asked.
         */
        private fun dayTarget(goal: Goal, threshold: Double?, last: LocalDate): Double? {
            if (goal.kind == GoalKind.Habit) {
                return threshold
            }

            val target = goal.target
            if (target.direction == GoalDirection.Reach) {
                return target.amount ?: target.targetAmount
            }

            val amount = target.amount ?: return null
            if (target.countsDays) {
                return amount
            }

            val length = when (target.period) {
                GoalPeriod.Week -> 7
                GoalPeriod.TwoWeeks -> 14
                GoalPeriod.Month -> last.lengthOfMonth()
                GoalPeriod.Day -> 1
            }
            return amount / length
        }

        private fun targetLabel(goal: Goal, dayTarget: Double, threshold: Double?): String {
            if (goal.kind == GoalKind.Habit) {
                return "target ${GoalTexts.minutes(threshold ?: dayTarget)}"
            }

            val target = goal.target
            val value = GoalTexts.shortValue(dayTarget, target.unit)
            return when {
                target.direction == GoalDirection.Reach -> "target $value"
                !target.countsDays -> "pace $value a day"
                target.direction == GoalDirection.AtMost -> "at most $value"
                else -> "target $value"
            }
        }

        // The chart's bottom and top values. Bars stand on zero, with room above the target; a level goal's chart frames its values and
        // its target instead; a habit ticked by hand is all or nothing.
        private fun scale(values: List<Double>, dayTarget: Double?, isLevel: Boolean, ticksOnly: Boolean): Pair<Double, Double> {
            if (ticksOnly) {
                return 0.0 to 1.0
            }

            if (isLevel) {
                val all = values + listOfNotNull(dayTarget)
                if (all.isEmpty()) {
                    return 0.0 to 1.0
                }

                val low = all.min()
                val high = all.max()
                val margin = maxOf(high - low, 1.0) * LEVEL_MARGIN
                return (low - margin) to (high + margin)
            }

            val top = maxOf((dayTarget ?: 0.0) * TARGET_HEADROOM, values.maxOrNull() ?: 0.0)
            return 0.0 to (if (top > 0) top else 1.0)
        }
    }
}
