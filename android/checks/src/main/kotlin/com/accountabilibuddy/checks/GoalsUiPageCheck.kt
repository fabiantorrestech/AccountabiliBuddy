package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDayStatus
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalMenuItemId
import com.accountabilibuddy.shared.goals.GoalMenuPlan
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalReadings
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalTrendBanner
import com.accountabilibuddy.shared.goals.GoalTrendRange
import com.accountabilibuddy.shared.goals.GoalTrends
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * Round 2 R6b1: the goal page's phone-only rules, checked on their own (the PC has no readings list or trend chart, so there is no C#
 * twin; the tiles, day panel, source lines, What it takes and heatmap are the parity fixture [GoalsUiDetailCheck]):
 * - the readings list: newest first, one reading charted per day (the latest hand edit, else the first reading: the numbers' own
 *   rule), every value of a day kept and listed, readings from a source the goal hasn't switched on left out;
 * - the trend: the charted days, the 3-month pace and its projection, the banner, the ranges, the 30-day change;
 * - the goal menu: the same rows in the same order as the PC's `GoalsViewModel.GoalMenu`, per kind.
 */
object GoalsUiPageCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val zone = offset
    private val today = LocalDate.of(2026, 11, 11)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun rule(type: GoalSourceType, n: Int = 900 + type.ordinal, slug: String? = null) = GoalSourceRule(id(n), type, beeminderSlug = slug)

    private fun goal(
        n: Int, kind: GoalKind, target: GoalTarget, sources: List<GoalSourceRule>, builtIn: Boolean = false, showOnToday: Boolean = false, pauses: List<GoalPauseStretch>? = null,
    ) = Goal(id(n), id(1), "Goal $n", "school", "#2E7D6B", kind, target, sources, now.minusDays(200), n.toDouble(), now.minusDays(200), builtIn = builtIn, showOnToday = showOnToday, pauses = pauses)

    // A weight to lose: 190 down to 175 by 2027-01-31, hand entries only (the weight source is "Coming soon").
    private fun weight(sources: List<GoalSourceRule> = listOf(rule(GoalSourceType.Hand))) =
        goal(10, GoalKind.Amount, GoalTarget(GoalPeriod.Day, null, GoalUnit.Lb, GoalDirection.Reach, targetAmount = 175.0, targetDate = LocalDate.of(2027, 1, 31)), sources)

    private fun entry(n: Int, goalId: UUID, day: LocalDate, time: LocalTime?, value: Double, source: GoalEntrySource, madeAt: OffsetDateTime = now) =
        GoalEntry(id(n), goalId, day, time, value, source, madeAt)

    private fun data(entries: List<GoalEntry>) = GoalSourceData(emptyList(), entries, zone)

    fun run(report: CheckReport) {
        val name = "goals-ui/page"
        readings(report, name)
        trend(report, name)
        menu(report, name)
    }

    private fun readings(report: CheckReport, name: String) {
        val steps = weight(listOf(rule(GoalSourceType.Hand), rule(GoalSourceType.HealthSteps)))
        val d = today
        val entries = listOf(
            entry(1, steps.id, d, LocalTime.of(7, 12), 182.4, GoalEntrySource.HealthConnect),
            entry(2, steps.id, d, LocalTime.of(9, 0), 182.6, GoalEntrySource.HealthConnect),
            entry(3, steps.id, d.minusDays(1), LocalTime.of(8, 0), 183.4, GoalEntrySource.HealthConnect),
            entry(4, steps.id, d.minusDays(1), null, 183.0, GoalEntrySource.Hand, now.minusDays(1).withHour(10)),
            entry(5, steps.id, d.minusDays(1), null, 183.2, GoalEntrySource.Hand, now.minusDays(1).withHour(11)),
            entry(6, steps.id, d.minusDays(2), LocalTime.of(6, 0), 184.0, GoalEntrySource.ScreenTime), // a source the goal hasn't switched on
            entry(7, id(77), d, LocalTime.of(6, 0), 1.0, GoalEntrySource.Hand), // another goal's
        )
        val list = GoalReadings.of(steps, entries, zone)
        report.check("$name: readings list only what counts for the goal (its own, hand and switched-on sources)", list.map { it.entry.id }.toSet() == setOf(id(1), id(2), id(3), id(4), id(5)))
        report.check(
            "$name: readings newest first (day, then time: a hand entry's time is when it was made)",
            list.map { it.entry.id } == listOf(id(2), id(1), id(5), id(4), id(3)),
            "got ${list.map { it.entry.id.toString().takeLast(2) }}",
        )
        val byId = list.associateBy { it.entry.id }
        report.check("$name: no hand entry that day: the first reading of the day is charted, a later one is not", byId.getValue(id(1)).charted && !byId.getValue(id(2)).charted)
        report.check("$name: the later reading says an earlier one is used", byId.getValue(id(2)).note == "not charted, an earlier reading is used")
        report.check("$name: the day's first reading says so", byId.getValue(id(1)).note == "charted, first of the day")
        report.check(
            "$name: a hand edit wins over a reading, and the latest hand edit over an older one",
            byId.getValue(id(5)).charted && !byId.getValue(id(4)).charted && !byId.getValue(id(3)).charted,
        )
        report.check("$name: notes for the losers say why", byId.getValue(id(4)).note == "not charted, a later edit wins" && byId.getValue(id(3)).note == "not charted, your edit wins")
        report.check("$name: the chart value is the charted reading's (the numbers' own rule)", GoalCounting.day(steps, data(entries), d.minusDays(1), d).value == 183.2 && GoalCounting.day(steps, data(entries), d, d).value == 182.4)
        report.check("$name: several values a day are all kept", list.count { it.day == d } == 2 && list.count { it.day == d.minusDays(1) } == 3)
        report.check("$name: a source's text", list.first { it.entry.id == id(1) }.source == "Health Connect" && list.first { it.entry.id == id(5) }.source == "Typed in")
        val weightOnly = GoalReadings.of(weight(), entries, zone)
        report.check("$name: a hand-only goal lists hand entries only (weight comes from the hand until ADR 81)", weightOnly.all { it.entry.source == GoalEntrySource.Hand } && weightOnly.size == 2)
    }

    private fun trend(report: CheckReport, name: String) {
        val g = weight()
        // 190 lb 150 days ago to 182.4 now at ~0.5 lb a week, a hand entry each week.
        val entries = (0..20).map { w ->
            entry(100 + w, g.id, today.minusDays(7L * w), null, 182.4 + 0.5 * w, GoalEntrySource.Hand, now.minusDays(7L * w))
        }
        val d = data(entries)
        val all = GoalTrends.build(g, d, today, GoalTrendRange.All)
        val three = GoalTrends.build(g, d, today, GoalTrendRange.ThreeMonths)
        val one = GoalTrends.build(g, d, today, GoalTrendRange.OneMonth)
        report.check("$name: the trend charts each day's value, oldest first", all.points.size == 21 && all.points.first().day < all.points.last().day && all.points.last().value == 182.4)
        report.check("$name: ranges cut the days (1 month, 3 months, All)", one.points.size == 5 && three.points.size == 14 && all.points.size == 21, "${one.points.size} ${three.points.size} ${all.points.size}")
        report.check("$name: the latest value ignores the range", one.latest == 182.4 && all.latest == 182.4)
        report.check("$name: the 30-day change reads toward the target (a loss for a weight to lose)", all.changeText == "−2 lb in 30 days" && all.changeGood == true, all.changeText ?: "null")
        report.check("$name: the projection starts at the latest charted day and ends at the target level", all.projection.size == 2 && all.projection.first().day == today && all.projection.last().value == 175.0)
        val finish = all.projection.last().day
        report.check("$name: the finish is the remaining over the 13-week pace (7.4 lb at ~0.5 lb/week = ~104 days)", finish == today.plusDays(104) || finish == today.plusDays(103) || finish == today.plusDays(105), finish.toString())
        report.check("$name: behind the target date: an amber banner with the days late", all.banner?.tone == GoalTrendBanner.Tone.Amber && all.banner?.headline?.endsWith("behind your target") == true, all.banner?.headline ?: "null")
        val onTrack = GoalTrends.build(g.copy(target = g.target.copy(targetDate = LocalDate.of(2027, 8, 1))), d, today, GoalTrendRange.All)
        report.check("$name: ahead of the target date: a teal banner", onTrack.banner?.tone == GoalTrendBanner.Tone.Teal && onTrack.banner?.body?.contains("before your target") == true)
        val few = GoalTrends.build(g, data(entries.take(1)), today, GoalTrendRange.All)
        report.check("$name: with one reading there is no pace, no projection, and the banner says so", few.projection.isEmpty() && few.banner?.tone == GoalTrendBanner.Tone.Neutral && few.changeText == null)
        val met = GoalTrends.build(g, data(listOf(entry(1, g.id, today, null, 174.0, GoalEntrySource.Hand), entry(2, g.id, today.minusDays(7), null, 177.0, GoalEntrySource.Hand))), today, GoalTrendRange.All)
        report.check("$name: a target already reached says so and draws no projection", met.projection.isEmpty() && met.banner?.headline == "Target reached")
        val none = GoalTrends.build(g.copy(target = g.target.copy(targetAmount = null, targetDate = null)), d, today, GoalTrendRange.All)
        report.check("$name: no target: no projection, no banner, no good or bad change", none.projection.isEmpty() && none.banner == null && none.changeGood == null)
        val empty = GoalTrends.build(g, data(emptyList()), today, GoalTrendRange.All)
        report.check("$name: no reading at all: nothing charted, no latest value", empty.points.isEmpty() && empty.latest == null)
    }

    private fun menu(report: CheckReport, name: String) {
        val habit = goal(1, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), listOf(rule(GoalSourceType.Hand)), showOnToday = true)
        val amount = goal(2, GoalKind.Amount, GoalTarget(GoalPeriod.Day, 8000.0, GoalUnit.Steps), listOf(rule(GoalSourceType.HealthSteps)))
        val time = goal(3, GoalKind.Time, GoalTarget(GoalPeriod.Week, 300.0, GoalUnit.Minutes), listOf(rule(GoalSourceType.FocusLabel)))
        val milestone = goal(4, GoalKind.Milestone, GoalTarget(GoalPeriod.Day, null, GoalUnit.Count), listOf(rule(GoalSourceType.Hand)))
        val deep = goal(5, GoalKind.Time, GoalTarget(GoalPeriod.Day, 120.0, GoalUnit.Minutes), listOf(rule(GoalSourceType.FocusAll)), builtIn = true)
        val linked = goal(6, GoalKind.Time, GoalTarget(GoalPeriod.Day, 30.0, GoalUnit.Minutes), listOf(rule(GoalSourceType.Beeminder, slug = "read")))
        fun ids(goal: Goal, paused: Boolean = false): List<GoalMenuItemId> {
            val state = GoalCounting.day(goal, data(emptyList()), today, today)
            return GoalMenuPlan.of(goal, state, paused, 0, "Learning").map { it.id }
        }

        // The order in the PC's GoalsViewModel.GoalMenu: heading, Start a session | quick log | Edit, Reminders, Move, Show on Today, Pause | Delete.
        val tail = listOf(
            GoalMenuItemId.Separator, GoalMenuItemId.EditGoal, GoalMenuItemId.Reminders, GoalMenuItemId.MoveToArea, GoalMenuItemId.ShowOnToday, GoalMenuItemId.Pause, GoalMenuItemId.Separator,
        )
        fun pc(log: List<GoalMenuItemId>, last: GoalMenuItemId) = listOf(GoalMenuItemId.Heading, GoalMenuItemId.StartSession, GoalMenuItemId.Separator) + log + tail + last
        report.check("$name: menu of a habit, in the PC's order", ids(habit) == pc(listOf(GoalMenuItemId.TickToday), GoalMenuItemId.Delete))
        report.check("$name: menu of an amount goal, in the PC's order", ids(amount) == pc(listOf(GoalMenuItemId.LogValue), GoalMenuItemId.Delete))
        report.check("$name: menu of a time goal, in the PC's order", ids(time) == pc(listOf(GoalMenuItemId.AddTimeByHand), GoalMenuItemId.Delete))
        report.check("$name: menu of a milestone, in the PC's order", ids(milestone) == pc(listOf(GoalMenuItemId.OpenTasks), GoalMenuItemId.Delete))
        report.check("$name: menu of a paused goal keeps the order (its quick log is greyed)", ids(habit, paused = true) == pc(listOf(GoalMenuItemId.TickToday), GoalMenuItemId.Delete))
        report.check("$name: menu of a Beeminder-linked goal adds Open in Beeminder after the quick log", ids(linked) == pc(listOf(GoalMenuItemId.AddTimeByHand, GoalMenuItemId.OpenInBeeminder), GoalMenuItemId.Delete))
        report.check("$name: Deep work's menu ends with Track time only, not Delete", ids(deep) == pc(listOf(GoalMenuItemId.AddTimeByHand), GoalMenuItemId.TrackTimeOnly))

        val habitItems = GoalMenuPlan.of(habit, GoalCounting.day(habit, data(emptyList()), today, today), false, 4, "Learning").associateBy { it.id }
        report.check("$name: Reminders shows how many are set, Move to area the current area, Show on Today its switch", habitItems.getValue(GoalMenuItemId.Reminders).value == "4 set" && habitItems.getValue(GoalMenuItemId.MoveToArea).value == "Learning" && habitItems.getValue(GoalMenuItemId.ShowOnToday).checked == true)
        report.check("$name: Delete is the destructive row", habitItems.getValue(GoalMenuItemId.Delete).danger && habitItems.values.count { it.danger } == 1)
        val deepItems = GoalMenuPlan.of(deep, GoalCounting.day(deep, data(emptyList()), today, today), false, 0, "Work").associateBy { it.id }
        report.check(
            "$name: Deep work refuses pause, hand time and a phone target change (greyed), as the PC does",
            !deepItems.getValue(GoalMenuItemId.Pause).enabled && !deepItems.getValue(GoalMenuItemId.AddTimeByHand).enabled && !deepItems.getValue(GoalMenuItemId.TrackTimeOnly).enabled &&
                deepItems.getValue(GoalMenuItemId.Reminders).value == "None",
        )
        val pausedItems = GoalMenuPlan.of(habit, GoalCounting.day(habit, data(emptyList()), today, today), true, 0, "Learning").associateBy { it.id }
        report.check("$name: a paused goal offers Resume and greys its quick log", pausedItems.getValue(GoalMenuItemId.Pause).title == "Resume goal" && !pausedItems.getValue(GoalMenuItemId.TickToday).enabled)
        report.check("$name: Open its tasks is Coming soon (greyed)", GoalMenuPlan.of(milestone, GoalCounting.day(milestone, data(emptyList()), today, today), false, 0, "A").first { it.id == GoalMenuItemId.OpenTasks }.let { !it.enabled && it.note == "Coming soon" })
        val untick = GoalMenuPlan.of(habit, GoalCounting.day(habit, data(listOf(entry(1, habit.id, today, null, 1.0, GoalEntrySource.Hand))), today, today), false, 0, "A").first { it.id == GoalMenuItemId.TickToday }
        report.check("$name: a ticked habit offers Untick today", untick.title == "Untick today" && GoalCounting.day(habit, data(listOf(entry(1, habit.id, today, null, 1.0, GoalEntrySource.Hand))), today, today).status != GoalDayStatus.Missed)
    }
}
