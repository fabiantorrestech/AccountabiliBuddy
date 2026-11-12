package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalDraftText
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalExtraTiles
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalMenuItemId
import com.accountabilibuddy.shared.goals.GoalMenuPlan
import com.accountabilibuddy.shared.goals.GoalOrderRules
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalReadings
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalSources
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalTodayControl
import com.accountabilibuddy.shared.goals.GoalTodayRows
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * The phone design-fix round's pure rules, checked on their own (no fixture: the PC rules they follow live in view models, not in a
 * pure function). Each line names the rule it follows:
 * - Today's goals ([GoalTodayRows]): which goals show is the PC's `TodayGoalsViewModel.Refresh` (Show on Today, live, not paused
 *   today, list order); the rows' controls, lines and "n left" are the phone boards' (Main, B-Phone-Tray-Open);
 * - the order of an area's goals and where a deleted area's goals go ([GoalOrderRules], the areas' own `GoalAreaOrder` pattern);
 * - notes and the goal's own Extra tiles in the editor's draft ([GoalDrafts], the PC's Edit goal fields);
 * - the goal menu's "Start a session…" row and the readings list's "was <value>" ([GoalMenuPlan], [GoalReadings]).
 */
object GoalsUiFixRoundCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val today = LocalDate.of(2026, 11, 11) // a Wednesday
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)
    private val areaId = id(1)

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun hand() = GoalSourceRule(id(900), GoalSourceType.Hand)

    private fun goal(
        n: Int, kind: GoalKind, target: GoalTarget, shown: Boolean = true, area: UUID = areaId, position: Double = n.toDouble(),
        pauses: List<GoalPauseStretch>? = null, sources: List<GoalSourceRule> = listOf(hand()), builtIn: Boolean = false,
    ) = Goal(id(n), area, "Goal $n", "school", "#2E7D6B", kind, target, sources, now.minusDays(200), position, now.minusDays(200), builtIn = builtIn, showOnToday = shown, pauses = pauses)

    private fun habit(n: Int, shown: Boolean = true, pauses: List<GoalPauseStretch>? = null) =
        goal(n, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), shown, pauses = pauses)

    private fun weight(n: Int) =
        goal(n, GoalKind.Amount, GoalTarget(GoalPeriod.Day, null, GoalUnit.Lb, GoalDirection.Reach, targetAmount = 175.0, targetDate = LocalDate.of(2027, 1, 31)))

    private fun time(n: Int) = goal(n, GoalKind.Time, GoalTarget(GoalPeriod.Day, 120.0, GoalUnit.Minutes))

    private fun entry(n: Int, goalId: UUID, day: LocalDate, value: Double, source: GoalEntrySource = GoalEntrySource.Hand, time: LocalTime? = null, madeAt: OffsetDateTime = now) =
        GoalEntry(id(n), goalId, day, time, value, source, madeAt)

    private fun facts(goals: List<Goal>, entries: List<GoalEntry>): List<GoalFacts> {
        val data = GoalSourceData(emptyList(), entries, offset)
        return goals.map { GoalFactsBuilder.build(it, data, today, GoalWeekStart.Sunday) }
    }

    fun run(report: CheckReport) {
        todayRows(report, "goals-ui/today")
        order(report, "goals-ui/order")
        draft(report, "goals-ui/draft")
        menuAndReadings(report, "goals-ui/menu")
    }

    private fun todayRows(report: CheckReport, name: String) {
        val paused = habit(4, pauses = listOf(GoalPauseStretch(today.minusDays(3))))
        val goals = listOf(habit(2), habit(3, shown = false), paused, weight(5), time(6))
        val area: (UUID) -> String? = { "Learning" }

        val empty = GoalTodayRows.of(facts(goals, emptyList()), area, inSession = false)
        report.check("$name: Show on Today, live and not paused today, in the list's order (the PC's TodayGoalsViewModel.Refresh)", empty.rows.map { it.goalId } == listOf(id(2), id(5), id(6)), "got ${empty.rows.map { it.goalId.toString().takeLast(2) }}")
        report.check("$name: a habit ticks, a level to reach is logged, time is a bar", empty.rows.map { it.control } == listOf(GoalTodayControl.Tick, GoalTodayControl.Log, GoalTodayControl.Bar))
        report.check("$name: nothing done yet: every row is left (\"3 left\")", empty.left == 3 && empty.leftText == "3 left")
        report.check("$name: an unticked habit names its area", empty.rows[0].subLine == "Learning" && !empty.rows[0].checked, "got ${empty.rows[0].subLine}")
        report.check("$name: a value never logged says so", empty.rows[1].subLine == "Nothing logged yet", "got ${empty.rows[1].subLine}")

        val entries = listOf(
            entry(1, id(2), today, 1.0),
            entry(2, id(5), today.minusDays(2), 182.4),
            entry(3, id(6), today, 70.0),
        )
        val some = GoalTodayRows.of(facts(goals, entries), area, inSession = false)
        report.check("$name: a habit ticked today is checked and done", some.rows[0].checked && some.rows[0].done)
        report.check("$name: a weight last logged two days ago reads \"Not logged since Mon\" and is still left", some.rows[1].subLine == "Not logged since Mon" && !some.rows[1].done, "got ${some.rows[1].subLine}")
        report.check("$name: time reads \"1h 10m of 2h · 50 min to go\" with its share on the bar", some.rows[2].subLine == "1h 10m of 2h · 50 min to go" && kotlin.math.abs(some.rows[2].fraction - 70.0 / 120.0) < 1e-9, "got ${some.rows[2].subLine} ${some.rows[2].fraction}")
        report.check("$name: \"2 left\" once the habit is ticked", some.left == 2)

        val all = GoalTodayRows.of(facts(goals, listOf(entry(1, id(2), today, 1.0), entry(2, id(5), today, 182.0), entry(3, id(6), today, 120.0))), area, inSession = false)
        report.check("$name: a value logged today is done (\"Logged today · 182 lb\")", all.rows[1].done && all.rows[1].subLine.startsWith("Logged today · 182"), "got ${all.rows[1].subLine}")
        report.check("$name: everything done reads \"all done\"", all.left == 0 && all.leftText == "all done" && all.rows[2].subLine.endsWith("· done"), "got ${all.rows[2].subLine}")

        val streak = GoalTodayRows.of(facts(listOf(habit(2)), (0L..2L).map { entry(10 + it.toInt(), id(2), today.minusDays(it), 1.0) }), area, inSession = false)
        report.check("$name: a daily habit's line carries its streak (\"Learning · 3-day streak\")", streak.rows.single().subLine == "Learning · 3-day streak", "got ${streak.rows.single().subLine}")

        val deep = goal(7, GoalKind.Time, GoalTarget(GoalPeriod.Day, 120.0, GoalUnit.Minutes), sources = listOf(GoalSources.DEEP_WORK_RULE), builtIn = true)
        val inSession = GoalTodayRows.of(facts(listOf(deep), emptyList()), area, inSession = true).rows.single()
        val idle = GoalTodayRows.of(facts(listOf(deep), emptyList()), area, inSession = false).rows.single()
        report.check("$name: in a session Deep work's line says the session counts (board B-Phone-Tray-Open)", inSession.subLine.endsWith("· this session counts") && inSession.isDeepWork, "got ${inSession.subLine}")
        report.check("$name: outside a session Deep work says what is left", idle.subLine.endsWith("to go") && idle.control == GoalTodayControl.Bar, "got ${idle.subLine}")
        report.check("$name: no goal with Show on Today on = no rows", GoalTodayRows.of(facts(listOf(habit(3, shown = false)), emptyList()), area, false).rows.isEmpty())
    }

    private fun order(report: CheckReport, name: String) {
        val other = id(8)
        val goals = listOf(
            goal(11, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), position = 3.0),
            goal(12, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), position = 1.0),
            goal(13, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), position = 2.0),
            goal(14, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), area = other, position = 5.0),
            goal(15, GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), position = 9.0).copy(deletedAt = now),
        )
        report.check("$name: an area's live goals, lowest position first", GoalOrderRules.inArea(goals, areaId).map { it.id } == listOf(id(12), id(13), id(11)))
        val moved = GoalOrderRules.reordered(goals, areaId, listOf(id(11), id(12), id(13)))
        report.check(
            "$name: a new order gives positions 1..N and saves only the goals that moved (the areas' own rule)",
            moved.associate { it.id to it.position } == mapOf(id(11) to 1.0, id(12) to 2.0, id(13) to 3.0),
            "got ${moved.map { it.id.toString().takeLast(2) to it.position }}",
        )
        report.check("$name: the same order moves nothing", GoalOrderRules.reordered(goals, areaId, listOf(id(12), id(13), id(11))).isEmpty())
        report.check("$name: a goal that keeps its place isn't saved", GoalOrderRules.reordered(goals, areaId, listOf(id(12), id(11), id(13))).map { it.id to it.position } == listOf(id(11) to 2.0, id(13) to 3.0))
        report.check("$name: an order that doesn't name exactly the area's goals changes nothing", GoalOrderRules.reordered(goals, areaId, listOf(id(11), id(12))).isEmpty() && GoalOrderRules.reordered(goals, areaId, listOf(id(11), id(12), id(14))).isEmpty())
        report.check("$name: a reorder changes only the place", moved.all { m -> goals.first { it.id == m.id }.copy(position = m.position) == m })

        val areas = listOf(
            GoalArea(areaId, "Learning", "school", "#2E7D6B", 2.0, now),
            GoalArea(other, "Work", "work", "#2E7D6B", 1.0, now),
            GoalArea(id(9), "Gone", "home", "#2E7D6B", 0.5, now, now),
        )
        report.check("$name: a deleted area's goals go to the first other live area", GoalOrderRules.heirOf(areas, areaId)?.id == other && GoalOrderRules.heirOf(areas, other)?.id == areaId)
        report.check("$name: with no other live area there is nowhere to go", GoalOrderRules.heirOf(areas.filter { it.id != other }, areaId) == null)
        val rehomed = GoalOrderRules.movedTo(goals, areaId, other)
        report.check(
            "$name: moved goals keep their order, after the heir's own goals",
            rehomed.map { it.id } == listOf(id(12), id(13), id(11)) && rehomed.map { it.position } == listOf(6.0, 7.0, 8.0) && rehomed.all { it.areaId == other },
            "got ${rehomed.map { it.position }}",
        )
    }

    private fun draft(report: CheckReport, name: String) {
        val stored = habit(2).copy(notes = "Why: the trip in May", extraTiles = GoalExtraTiles(streak = true))
        val draft = GoalDrafts.fromGoal(stored)
        report.check("$name: a goal's notes and its own extra tiles come into the draft", draft.notes == "Why: the trip in May" && draft.extraTiles == GoalExtraTiles(streak = true))
        val saved = GoalDrafts.toGoal(draft.copy(notes = "  New reason  "), stored, now, 1.0)
        report.check("$name: notes are saved trimmed", saved?.notes == "New reason" && saved.extraTiles == GoalExtraTiles(streak = true))
        report.check("$name: blank notes are saved as none", GoalDrafts.toGoal(draft.copy(notes = "   "), stored, now, 1.0)?.notes == null)
        report.check("$name: notes are cut at the limit", GoalDrafts.notesOf(draft.copy(notes = "x".repeat(GoalDrafts.MAX_NOTES_LENGTH + 50)))?.length == GoalDrafts.MAX_NOTES_LENGTH)

        val plain = GoalDrafts.fromGoal(habit(3))
        report.check("$name: a goal with no choice of its own follows Goals Options (null)", plain.extraTiles == null && GoalDrafts.toGoal(plain, habit(3), now, 1.0)?.extraTiles == null)
        val global = GoalExtraTiles(streak = true, total = true)
        val off = GoalDrafts.withExtraTile(plain, global, streak = false)
        report.check("$name: the goal's own switch wins and keeps the other tile as it showed", off.extraTiles == GoalExtraTiles(streak = false, total = true))
        report.check("$name: a second flip keeps the first", GoalDrafts.withExtraTile(off, global, total = false).extraTiles == GoalExtraTiles.NONE)

        val deep = goal(7, GoalKind.Time, GoalTarget(GoalPeriod.Day, null, GoalUnit.Minutes), sources = listOf(GoalSources.DEEP_WORK_RULE), builtIn = true)
        val deepSaved = GoalDrafts.toGoal(GoalDrafts.fromGoal(deep).copy(notes = "Mornings", extraTiles = GoalExtraTiles(total = true)), deep, now, 1.0)
        report.check("$name: Deep work takes notes and extra tiles too (nothing else of it changes)", deepSaved == deep.copy(notes = "Mornings", extraTiles = GoalExtraTiles(total = true)))

        val turned = GoalDraftText.read(GoalDraftText.write(off.copy(notes = "line one\nline \"two\"")))
        report.check("$name: the half-made goal survives a screen turn with its notes and tiles", turned == off.copy(notes = "line one\nline \"two\""))
        report.check("$name: and with no tile choice", GoalDraftText.read(GoalDraftText.write(plain)) == plain)
    }

    private fun menuAndReadings(report: CheckReport, name: String) {
        val habit = habit(2)
        val state = GoalCounting.day(habit, GoalSourceData(emptyList(), emptyList(), offset), today, today)
        val open = GoalMenuPlan.of(habit, state, false, 0, "Learning", startValue = "Default").first { it.id == GoalMenuItemId.StartSession }
        report.check("$name: Start a session… shows the default and opens the presets (board Goals-Phone-Goal-Menu)", open.title == "Start a session…" && open.value == "Default" && open.enabled && open.opensMore && open.note == "Counts toward Goal 2")
        val blocked = GoalMenuPlan.of(habit, state, false, 0, "Learning", startValue = "Default", startBlocked = "A session is running").first { it.id == GoalMenuItemId.StartSession }
        report.check("$name: when a session can't be started the row is greyed and says why", !blocked.enabled && blocked.note == "A session is running" && blocked.value == null)

        val weight = weight(5).copy(sources = listOf(hand(), GoalSourceRule(id(901), GoalSourceType.HealthSteps)))
        val entries = listOf(
            entry(1, weight.id, today, 182.8, GoalEntrySource.HealthConnect, LocalTime.of(7, 12)),
            entry(2, weight.id, today, 182.0, madeAt = now),
            entry(3, weight.id, today.minusDays(1), 183.0, madeAt = now.minusDays(1)),
        )
        val list = GoalReadings.of(weight, entries, offset).associateBy { it.entry.id }
        report.check("$name: the edit the chart uses says what it replaced (\"was 182.8\")", list.getValue(id(2)).charted && list.getValue(id(2)).replaced == 182.8, "got ${list.getValue(id(2)).replaced}")
        report.check("$name: a reading that isn't the charted edit, and an edit that replaced nothing, say nothing", list.getValue(id(1)).replaced == null && list.getValue(id(3)).replaced == null)
    }
}
