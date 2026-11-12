package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHeatCellMark
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalStatsDayMark
import com.accountabilibuddy.shared.goals.GoalStatsHistory
import com.accountabilibuddy.shared.goals.GoalStatsSeries
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlin.math.abs

/**
 * Round 2 R6b4: the phone's Stats per goal, the rules with no C# twin (the PC's Stats draws a goal's year only; its words and that
 * year are the parity fixture [GoalsUiStatsCheck]): [GoalStatsSeries], a bar per day of a range with its target line and numbers.
 * - a paused stretch is hatched and left out of the days hit, the total and the average, whatever was measured on it;
 * - the marks follow the heatmap's (hit, some, missed, today, nothing for a day not asked or still to come);
 * - the target line: a daily amount, "at most", a weekly amount as a pace a day, a level goal's level on a chart that frames its values;
 * - the paused stretches in words, and the year's hatched cells on a grid wide enough for the whole year.
 */
object GoalsUiStatsSeriesCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val today = LocalDate.of(2026, 11, 11)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)
    private val from = LocalDate.of(2026, 10, 29)

    // Paused Nov 3 to Nov 6 (turned back on Nov 7).
    private val pause = listOf(GoalPauseStretch(LocalDate.of(2026, 11, 3), LocalDate.of(2026, 11, 7)))

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun goal(n: Int, kind: GoalKind, target: GoalTarget, pauses: List<GoalPauseStretch>? = null) = Goal(
        id(n), id(1), "Goal $n", "school", "#2E7D6B", kind, target, listOf(GoalSourceRule(id(900), GoalSourceType.Hand)),
        now.minusDays(200), n.toDouble(), now.minusDays(200), pauses = pauses,
    )

    private fun day(month: Int, dayOfMonth: Int): LocalDate = LocalDate.of(2026, month, dayOfMonth)

    private fun data(goal: Goal, values: Map<LocalDate, Double>): GoalSourceData {
        val entries = values.entries.mapIndexed { index, (day, value) -> GoalEntry(id(100 + index), goal.id, day, null, value, GoalEntrySource.Hand, now) }
        return GoalSourceData(emptyList(), entries, offset)
    }

    private fun near(a: Double?, b: Double): Boolean = a != null && abs(a - b) < 1e-9

    fun run(report: CheckReport) {
        val name = "goals-ui/stats-series"
        habit(report, name)
        time(report, name)
        weekly(report, name)
        level(report, name)
        edges(report, name)
        words(report, name)
    }

    private fun habit(report: CheckReport, name: String) {
        val goal = goal(20, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), pause)
        // Nov 4 was ticked while paused: it stays paused.
        val ticks = listOf(day(10, 29), day(10, 30), day(11, 1), day(11, 4), day(11, 7), day(11, 8), day(11, 10)).associateWith { 1.0 }
        val series = GoalStatsSeries.build(goal, data(goal, ticks), from, today, today)
        val marks = series.days.map { it.mark }
        val expected = listOf(
            GoalStatsDayMark.Hit, GoalStatsDayMark.Hit, GoalStatsDayMark.Missed, GoalStatsDayMark.Hit, GoalStatsDayMark.Missed,
            GoalStatsDayMark.Paused, GoalStatsDayMark.Paused, GoalStatsDayMark.Paused, GoalStatsDayMark.Paused,
            GoalStatsDayMark.Hit, GoalStatsDayMark.Hit, GoalStatsDayMark.Missed, GoalStatsDayMark.Hit, GoalStatsDayMark.Today,
        )
        report.check("$name: a habit's days are hit, missed, paused (hatched) or today, one per day of the range", marks == expected, "got $marks")
        report.check(
            "$name: a paused stretch is hatched for each of its days (Nov 3 to Nov 6); the day it was turned back on counts again",
            series.days.filter { it.mark == GoalStatsDayMark.Paused }.map { it.day } == listOf(day(11, 3), day(11, 4), day(11, 5), day(11, 6)),
        )
        report.check(
            "$name: a paused day is neither hit nor missed: 6 of 9 days, 4 paused",
            series.hitDays == 6 && series.askedDays == 9 && series.pausedDays == 4,
            "hit ${series.hitDays} asked ${series.askedDays} paused ${series.pausedDays}",
        )
        report.check("$name: a tick made on a paused day draws nothing and counts nothing", series.days.first { it.day == day(11, 4) }.let { it.amount == 0.0 && it.fraction == 0.0 })
        report.check(
            "$name: the headline says how many days were hit and that paused days are left out",
            series.big == "6 of 9" && series.note == "days hit · 4 paused days left out",
            "${series.big} | ${series.note}",
        )
        report.check(
            "$name: a habit ticked by hand has no target line, total or average; a hit day is a full bar",
            series.targetFraction == null && series.total == null && series.average == null && series.days[0].fraction == 1.0,
        )
        report.check("$name: its tiles: Days hit, Paused", series.tiles.map { it.title to it.value } == listOf("Days hit" to "6 of 9", "Paused" to "4 days"), "${series.tiles}")
        val pausedText = series.days.first { it.day == day(11, 5) }.text
        report.check("$name: a paused day's words say paused", pausedText.endsWith("· paused"), pausedText)

        // The year, as the PC's Stats draws it: the same four days hatched, on a grid wide enough for the whole year.
        val year = GoalHeatmap.build(goal, data(goal, ticks), today, GoalWeekStart.Sunday, GoalHeatmapRange.Year, today, 60)
        report.check(
            "$name: the goal's year hatches the paused stretch's days and no other",
            year.cells.filter { it.mark == GoalHeatCellMark.Paused }.mapNotNull { it.day }.sorted() == listOf(day(11, 3), day(11, 4), day(11, 5), day(11, 6)),
        )
        report.check(
            "$name: 60 columns hold the whole year (nothing cropped)",
            year.range == GoalHeatmapRange.Year && year.grid.rangeStart == LocalDate.of(2025, 11, 12) && year.grid.columns <= 54,
            "${year.range} ${year.grid.rangeStart} ${year.grid.columns}",
        )
    }

    private fun time(report: CheckReport, name: String) {
        val goal = goal(21, GoalKind.Time, GoalTarget(GoalPeriod.Day, 30.0, GoalUnit.Minutes), pause)
        // Nov 4's two hours were measured while paused.
        val minutes = mapOf(
            day(10, 29) to 30.0, day(10, 30) to 45.0, day(10, 31) to 15.0, day(11, 2) to 60.0, day(11, 4) to 120.0,
            day(11, 7) to 30.0, day(11, 9) to 20.0, day(11, 10) to 40.0, day(11, 11) to 10.0,
        )
        val series = GoalStatsSeries.build(goal, data(goal, minutes), from, today, today)
        val byDay = series.days.associateBy { it.day }
        report.check(
            "$name: a time goal's day at or over its target is hit, under it some, empty missed",
            byDay.getValue(day(10, 30)).mark == GoalStatsDayMark.Hit && byDay.getValue(day(10, 31)).mark == GoalStatsDayMark.Some &&
                byDay.getValue(day(11, 1)).mark == GoalStatsDayMark.Missed,
        )
        report.check("$name: the total leaves the paused stretch's time out (250 min, not 370)", near(series.total, 250.0), "total ${series.total}")
        report.check(
            "$name: the average is over the 9 whole days asked, paused days and today left out (240 / 9)",
            near(series.average, 240.0 / 9),
            "average ${series.average}",
        )
        report.check("$name: 5 of 9 days hit, 4 paused", series.hitDays == 5 && series.askedDays == 9 && series.pausedDays == 4)
        report.check(
            "$name: the target line sits at the daily amount on a chart as tall as the best day",
            near(series.targetFraction, 0.5) && series.targetLabel == "target 30 min",
            "${series.targetFraction} ${series.targetLabel}",
        )
        report.check("$name: a bar is its share of the chart's height", near(byDay.getValue(day(10, 30)).fraction, 0.75) && near(byDay.getValue(day(11, 2)).fraction, 1.0))
        report.check("$name: today is its own mark with what it has so far", byDay.getValue(today).let { it.mark == GoalStatsDayMark.Today && near(it.amount, 10.0) })
        report.check(
            "$name: its tiles: Days hit, Total, Average, Paused",
            series.tiles.map { it.title to it.value } == listOf("Days hit" to "5 of 9", "Total" to "4h 10m", "Average" to "26 min", "Paused" to "4 days"),
            "${series.tiles}",
        )

        // With no day over the target, the chart is a quarter taller than the target.
        val low = GoalStatsSeries.build(goal, data(goal, mapOf(day(10, 29) to 30.0)), from, today, today)
        report.check("$name: with no day over the target the line keeps room above it", near(low.targetFraction, 1 / GoalStatsSeries.TARGET_HEADROOM))

        val cap = goal(22, GoalKind.Amount, GoalTarget(GoalPeriod.Day, 120.0, GoalUnit.Minutes, GoalDirection.AtMost))
        val capped = GoalStatsSeries.build(cap, data(cap, mapOf(day(11, 9) to 90.0, day(11, 10) to 150.0)), day(11, 9), day(11, 10), today)
        report.check(
            "$name: an at-most goal: under the cap is hit, over it is not, the line says at most",
            capped.days.map { it.mark } == listOf(GoalStatsDayMark.Hit, GoalStatsDayMark.Some) && capped.targetLabel == "at most 2h",
            "${capped.days.map { it.mark }} ${capped.targetLabel}",
        )
    }

    private fun weekly(report: CheckReport, name: String) {
        val goal = goal(23, GoalKind.Time, GoalTarget(GoalPeriod.Week, 210.0, GoalUnit.Minutes))
        val series = GoalStatsSeries.build(goal, data(goal, mapOf(day(11, 9) to 60.0, day(11, 10) to 30.0)), day(11, 8), day(11, 14), today)
        report.check(
            "$name: a weekly amount: a day with time is some, an empty one and the days still to come are nothing, today is today",
            series.days.map { it.mark } == listOf(
                GoalStatsDayMark.None, GoalStatsDayMark.Some, GoalStatsDayMark.Some, GoalStatsDayMark.Today, GoalStatsDayMark.None, GoalStatsDayMark.None, GoalStatsDayMark.None,
            ),
            "${series.days.map { it.mark }}",
        )
        report.check(
            "$name: its line is the week's amount as a pace a day",
            series.targetLabel == "pace 30 min a day" && near(series.targetFraction, 0.5),
            "${series.targetLabel} ${series.targetFraction}",
        )
        report.check(
            "$name: its headline is the range's total",
            series.big == "1h 30m" && series.note == "counted in this range" && near(series.average, 45.0),
            "${series.big} | ${series.note} | ${series.average}",
        )
        report.check("$name: a day still to come says so and has no bar", series.days.last().let { it.text == "Sat 11/14 · still to come" && it.fraction == 0.0 }, series.days.last().text)
        report.check("$name: a range has a day for each of its dates", series.days.map { it.day } == (0..6).map { day(11, 8).plusDays(it.toLong()) })
    }

    private fun level(report: CheckReport, name: String) {
        val goal = goal(24, GoalKind.Amount, GoalTarget(GoalPeriod.Day, null, GoalUnit.Lb, GoalDirection.Reach, targetAmount = 175.0, targetDate = LocalDate.of(2027, 1, 31)))
        val series = GoalStatsSeries.build(goal, data(goal, mapOf(day(11, 9) to 183.0, day(11, 10) to 182.0, day(11, 11) to 181.0)), day(11, 5), today, today)
        val byDay = series.days.associateBy { it.day }
        report.check(
            "$name: a level goal's chart frames its values and its target (not from zero)",
            near(series.targetFraction, 1.2 / 10.4) && near(byDay.getValue(day(11, 9)).fraction, 9.2 / 10.4) && series.targetLabel == "target 175 lb",
            "${series.targetFraction} ${byDay.getValue(day(11, 9)).fraction} ${series.targetLabel}",
        )
        report.check(
            "$name: its headline is the latest value, with no total",
            series.big == "181 lb" && series.note == "latest value · Wed 11/11" && series.total == null,
            "${series.big} | ${series.note}",
        )
        report.check(
            "$name: its average is over the whole days with a value",
            near(series.average, 182.5) && series.tiles.map { it.title to it.value } == listOf("Average" to "182.5 lb"),
            "${series.average} ${series.tiles}",
        )
        report.check("$name: a day with no value has no bar", byDay.getValue(day(11, 6)).let { it.mark == GoalStatsDayMark.None && it.fraction == 0.0 })
    }

    private fun edges(report: CheckReport, name: String) {
        val goal = goal(25, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo))
        val created = goal.createdOn(offset)
        val before = GoalStatsSeries.build(goal, data(goal, emptyMap()), created.minusDays(20), created.minusDays(11), today)
        report.check(
            "$name: before the goal began nothing is asked: no bars, 0 of 0",
            before.days.size == 10 && before.days.all { it.mark == GoalStatsDayMark.None } && before.big == "0 of 0",
        )
        val coming = GoalStatsSeries.build(goal, data(goal, emptyMap()), today.plusDays(1), today.plusDays(7), today)
        report.check(
            "$name: a range still to come has its days and no numbers",
            coming.days.size == 7 && coming.days.all { it.mark == GoalStatsDayMark.None && it.text.endsWith("still to come") } && coming.askedDays == 0,
        )
        val never = GoalStatsSeries.build(goal, data(goal, emptyMap()), from, today, today)
        report.check(
            "$name: a goal never paused has no Paused tile and no note about it",
            never.pausedDays == 0 && never.tiles.none { it.title == "Paused" } && never.note == "days hit",
        )
    }

    private fun words(report: CheckReport, name: String) {
        val stretches = listOf(
            GoalPauseStretch(day(11, 10)),
            GoalPauseStretch(LocalDate.of(2025, 12, 30), day(1, 3)),
            GoalPauseStretch(day(11, 3), day(11, 3)),
            GoalPauseStretch(day(9, 14), day(10, 31)),
        )
        val goal = goal(26, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), stretches)
        val texts = GoalStatsHistory.pausedTexts(goal, today)
        report.check(
            "$name: paused stretches in words, oldest first (the last paused day is the day before it was turned back on)",
            texts == listOf("Paused Dec 30, 2025 – Jan 2", "Paused Sep 14 – Oct 30", "Paused Nov 3", "Paused since Tue 11/10"),
            "$texts",
        )
        report.check(
            "$name: a goal never paused has no paused stretches",
            GoalStatsHistory.pausedTexts(goal(27, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo)), today).isEmpty(),
        )
    }
}
