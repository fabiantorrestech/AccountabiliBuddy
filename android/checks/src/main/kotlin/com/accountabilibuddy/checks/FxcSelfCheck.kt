package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalReviewChoice
import com.accountabilibuddy.shared.goals.GoalReviewFacts
import com.accountabilibuddy.shared.goals.GoalReviewSuggestion
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalTodayRows
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * FX-C, the phone's own words for an "at most" goal (the shared tiles, day panel and legend are parity-checked against the PC in
 * `goals-ui/detail`): Today's goals card says what is left under the limit, and the limit never reads as a goal still to reach; the
 * weekly review's facts say "over the limit", not "behind".
 */
object FxcSelfCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val today = LocalDate.of(2026, 11, 11)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun goal(n: Int, kind: GoalKind, target: GoalTarget, daysOld: Long = 30, sources: List<GoalSourceRule> = listOf(GoalSourceRule(id(900), GoalSourceType.Hand))) =
        Goal(id(n), id(1), "Goal $n", "school", "#2E7D6B", kind, target, sources, now.minusDays(daysOld), n.toDouble(), now.minusDays(daysOld), showOnToday = true)

    private fun facts(goal: Goal, vararg values: Pair<LocalDate, Double>): GoalFacts {
        val entries = values.mapIndexed { index, (day, value) -> GoalEntry(id(100 + index), goal.id, day, null, value, GoalEntrySource.Hand, now) }
        return GoalFactsBuilder.build(goal, GoalSourceData(emptyList(), entries, offset), today, GoalWeekStart.Sunday)
    }

    private fun line(goal: Goal, used: Double): String = GoalTodayRows.of(listOf(facts(goal, today to used)), { null }, false).rows.single().subLine

    fun run(report: CheckReport) {
        val name = "fxc"
        val limit = goal(2, GoalKind.Time, GoalTarget(GoalPeriod.Day, 60.0, GoalUnit.Minutes, GoalDirection.AtMost))
        report.check("$name/today: 45 of an at most 1h reads \"15 min left\"", line(limit, 45.0) == "45 min of 1h · 15 min left", "got ${line(limit, 45.0)}")
        report.check("$name/today: 70 of an at most 1h reads \"over by 10 min\", never \"done\"", line(limit, 70.0) == "1h 10m of 1h · over by 10 min", "got ${line(limit, 70.0)}")
        report.check("$name/today: exactly at the limit reads \"at the limit\"", line(limit, 60.0) == "1h of 1h · at the limit", "got ${line(limit, 60.0)}")

        val row = GoalTodayRows.of(listOf(facts(limit, today to 70.0)), { null }, false)
        report.check("$name/today: an at most goal asks for nothing to do (not counted in \"n left\")", row.rows.single().done && row.left == 0)

        val atLeast = goal(3, GoalKind.Time, GoalTarget(GoalPeriod.Day, 60.0, GoalUnit.Minutes))
        report.check("$name/today: an at least goal still reads \"15 min to go\"", line(atLeast, 45.0) == "45 min of 1h · 15 min to go", "got ${line(atLeast, 45.0)}")

        val weekly = goal(4, GoalKind.Time, GoalTarget(GoalPeriod.Week, 300.0, GoalUnit.Minutes, GoalDirection.AtMost))
        report.check("$name/today: a weekly limit counts the week so far", line(weekly, 40.0).endsWith("· 4h 20m left"), "got ${line(weekly, 40.0)}")

        // S3: a limit made mid-week is judged and worded against its prorated cap (300 x 6 / 7 = 257.1; 150 used leaves 1h 47m).
        val midWeek = goal(7, GoalKind.Time, GoalTarget(GoalPeriod.Week, 300.0, GoalUnit.Minutes, GoalDirection.AtMost), daysOld = 2)
        report.check("$name/today: a weekly limit made on Monday reads against its prorated cap", line(midWeek, 150.0).endsWith("· 1h 47m left"), "got ${line(midWeek, 150.0)}")

        // S4: an at-most goal is never "behind" (the row's pill, the status words).
        val overYesterday = facts(limit, today.minusDays(1) to 70.0)
        report.check("$name/pill: an at-most goal that went over reads \"Over the limit\", not \"Behind\"", com.accountabilibuddy.shared.goals.GoalNumbers.pill(overYesterday)?.first == "Over the limit", "got ${com.accountabilibuddy.shared.goals.GoalNumbers.pill(overYesterday)}")
        report.check("$name/status: within / over the limit, and nothing for another goal", GoalTexts.limitStatus(limit, com.accountabilibuddy.shared.goals.GoalPeriodStatus.OnTrack) == "within the limit"
            && GoalTexts.limitStatus(limit, com.accountabilibuddy.shared.goals.GoalPeriodStatus.Behind) == "over the limit" && GoalTexts.limitStatus(atLeast, com.accountabilibuddy.shared.goals.GoalPeriodStatus.Behind) == null)

        // S2: a hand entry says it replaces Beeminder's value (owner-visible).
        val beeTime = goal(8, GoalKind.Time, GoalTarget(GoalPeriod.Day, 60.0, GoalUnit.Minutes), sources = listOf(GoalSourceRule(id(900), GoalSourceType.Hand), GoalSourceRule(id(901), GoalSourceType.Beeminder, beeminderSlug = "x")))
        val beeData = GoalSourceData(emptyList(), listOf(GoalEntry(id(300), beeTime.id, today, null, 90.0, GoalEntrySource.Beeminder, now)), offset)
        val withReading = com.accountabilibuddy.shared.goals.GoalCounting.day(beeTime, beeData, today, today)
        val noReading = com.accountabilibuddy.shared.goals.GoalCounting.day(beeTime, beeData, today.minusDays(1), today)
        report.check("$name/caption: with a Beeminder reading", GoalTexts.replacesBeeminder(beeTime, withReading) == "Replaces Beeminder's 1h 30m", "got ${GoalTexts.replacesBeeminder(beeTime, withReading)}")
        report.check("$name/caption: with no reading", GoalTexts.replacesBeeminder(beeTime, noReading) == "Replaces Beeminder for this day")
        report.check("$name/caption: no Beeminder source, no note", GoalTexts.replacesBeeminder(limit, withReading) == null)

        val coffees = goal(5, GoalKind.Amount, GoalTarget(GoalPeriod.Day, 3.0, GoalUnit.Count, GoalDirection.AtMost))
        report.check("$name/text: an amount limit reads in its unit", GoalTexts.limitLeft(coffees, 2.0) == "1 left" && GoalTexts.limitLeft(coffees, 5.0) == "over by 2", "got ${GoalTexts.limitLeft(coffees, 2.0)} / ${GoalTexts.limitLeft(coffees, 5.0)}")
        report.check("$name/text: the sentence form starts with a capital", GoalTexts.limitLeftSentence(limit, 70.0) == "Over by 10 min" && GoalTexts.limitLeftSentence(limit, 60.0) == "At the limit")
        report.check("$name/text: a habit is never a limit", !GoalTexts.isLimit(goal(6, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo, GoalDirection.AtMost))))

        val suggestion = GoalReviewSuggestion(GoalReviewChoice.KeepGoing, 2, 3, 2, null, null, null, false)
        val data = GoalSourceData(emptyList(), emptyList(), offset)
        val limitFacts = GoalReviewFacts.of(limit, suggestion, data, today).map { it.text }
        report.check("$name/review: an at most goal is over its limit, not behind", limitFacts.containsAll(listOf("2 days over the limit", "Over the limit in 2 of the last 3")), "got $limitFacts")
        val plainFacts = GoalReviewFacts.of(atLeast, suggestion, data, today).map { it.text }
        report.check("$name/review: an at least goal is still behind", plainFacts.containsAll(listOf("2 days behind", "Behind in 2 of the last 3")), "got $plainFacts")
    }
}
