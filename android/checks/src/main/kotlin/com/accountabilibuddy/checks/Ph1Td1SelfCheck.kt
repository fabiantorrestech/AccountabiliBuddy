package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.today.StepsMovedNote
import com.accountabilibuddy.companion.today.StepsMovedRules
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.today.TodayPlacement
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDayPanel
import com.accountabilibuddy.shared.goals.GoalDayPanelSlots
import com.accountabilibuddy.shared.goals.GoalDayPanels
import com.accountabilibuddy.shared.goals.GoalDayState
import com.accountabilibuddy.shared.goals.GoalDayStatus
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalStepsRules
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID

/** PH1 + TD1 on the phone: pure rules with no C# twin (the PC's own checks cover its side). */
object Ph1Td1SelfCheck {
    private val now = OffsetDateTime.parse("2026-11-13T10:00:00+00:00")
    private val today = LocalDate.of(2026, 11, 13)

    fun run(report: CheckReport) {
        dayPanel(report)
        hideEverything(report)
        moreTodayBar(report)
        stepsPartGone(report)
        stepsMovedCard(report)
    }

    private fun goal(kind: GoalKind, unit: GoalUnit, sources: List<GoalSourceRule>, deep: Boolean = false): Goal =
        GoalStepsRules.newGoal(UUID.randomUUID(), UUID.randomUUID(), 1.0, now).copy(
            kind = kind,
            target = GoalTarget(GoalPeriod.Day, if (kind == GoalKind.Habit) 1.0 else 30.0, unit),
            sources = sources,
            builtIn = deep,
            showOnToday = false,
        )

    private fun state(day: LocalDate, status: GoalDayStatus, hit: Boolean) =
        GoalDayState(day, if (hit) 30.0 else 10.0, if (hit) 30.0 else 10.0, true, hit, status, if (hit) 0.0 else 20.0, listOf(GoalSourceType.Hand), true, false)

    // PH1 2: the day panel reserves ONE height whatever the day and the goal kind; the value-by-hand form fits inside it.
    private fun dayPanel(report: CheckReport) {
        val hand = listOf(GoalSourceRule(UUID.randomUUID(), GoalSourceType.Hand))
        val kinds = listOf(
            "habit" to goal(GoalKind.Habit, GoalUnit.YesNo, hand),
            "time" to goal(GoalKind.Time, GoalUnit.Minutes, hand),
            "amount" to goal(GoalKind.Amount, GoalUnit.Count, hand),
            "weight" to goal(GoalKind.Amount, GoalUnit.Kg, hand),
            "milestone" to goal(GoalKind.Milestone, GoalUnit.Count, hand),
            "deep work" to goal(GoalKind.Time, GoalUnit.Minutes, listOf(GoalSourceRule(UUID.randomUUID(), GoalSourceType.FocusAll)), deep = true),
        )
        val heights = mutableSetOf<Int>()
        var panels = 0
        for ((name, g) in kinds) {
            val own = mutableSetOf<Int>()
            for (day in listOf(today, today.minusDays(1), today.minusDays(40))) {
                for ((status, hit) in listOf(GoalDayStatus.Counted to true, GoalDayStatus.InProgress to false, GoalDayStatus.Paused to false)) {
                    val panel: GoalDayPanel = GoalDayPanels.of(g, state(day, status, hit), today) { null }
                    own += GoalDayPanelSlots.reservedDp(panel)
                    panels++
                }
            }
            heights += own
            report.check("ph1/day-panel: $name has one reserved height on any day", own.size == 1, "heights=$own")
        }
        report.check("ph1/day-panel: $panels panels, one reserved height (${GoalDayPanelSlots.RESERVED_DP} dp)", heights == setOf(GoalDayPanelSlots.RESERVED_DP))
        report.check("ph1/day-panel: the value-by-hand form fits the reserved height", GoalDayPanelSlots.editFits())
    }

    // TD1 1: every part that does not carry a guard or an obligation can be hidden in both layouts, the session card and This session
    // included; the safety parts stay shown; hiding everything survives saving and reading.
    private fun hideEverything(report: CheckReport) {
        for (inSession in listOf(false, true)) {
            val label = if (inSession) "in a session" else "not in a session"
            var layout = TodayLayout(session = inSession)
            TodayItem.entries.forEach { layout = layout.withPlacement(it, TodayPlacement.Hidden) }
            val hideable = TodayItem.entries.filter { TodayPlacement.Hidden in it.allowed }
            report.check("td1/hide-all ($label): every hideable part is Hidden", hideable.all { layout.placementOf(it) == TodayPlacement.Hidden })
            val always = TodayItem.entries.filter { TodayPlacement.Hidden !in it.allowed }
            report.check(
                "td1/hide-all ($label): only the guard and obligation parts stay",
                always.toSet() == setOf(TodayItem.Guard, TodayItem.LunchChip, TodayItem.PcMissing, TodayItem.WorkUse, TodayItem.DayOff, TodayItem.Import) &&
                    always.all { layout.placementOf(it) != TodayPlacement.Hidden },
            )
            report.check(
                "td1/hide-all ($label): the session card and This session can be hidden",
                layout.placementOf(TodayItem.SessionCard) == TodayPlacement.Hidden && layout.placementOf(TodayItem.ThisSession) == TodayPlacement.Hidden,
            )
            val back = layout.withPlacement(TodayItem.SessionCard, TodayPlacement.Above)
            report.check("td1/hide-all ($label): a hidden session card comes back", back.placementOf(TodayItem.SessionCard) == TodayPlacement.Above)
            report.check(
                "td1/hide-all ($label): the session card never folds",
                layout.withPlacement(TodayItem.SessionCard, TodayPlacement.Fold).placementOf(TodayItem.SessionCard) != TodayPlacement.Fold,
            )
            val saved = TodayLayout.parse(layout.orderText(), layout.placementsText(), inSession)
            report.check("td1/hide-all ($label): hiding everything survives save and read", TodayItem.entries.all { saved.placementOf(it) == layout.placementOf(it) })
        }
        val idle = TodayLayout().orderFor(false)
        val session = TodayLayout(session = true).orderFor(true)
        report.check(
            "td1/order: the session card and This session head Today by default, in and out of a session",
            idle.take(2) == listOf(TodayItem.SessionCard, TodayItem.ThisSession) && session.take(2) == listOf(TodayItem.SessionCard, TodayItem.ThisSession),
        )
        val old = TodayLayout.parse("ring,goals,next-up", null)
        report.check(
            "td1/order: an order saved before them gets them first, not last",
            old.orderFor(false).take(2) == listOf(TodayItem.SessionCard, TodayItem.ThisSession) && old.orderFor(false).containsAll(TodayItem.entries),
        )
    }

    // TD1 2: the More today bar hidden = no chips; a part that can't be hidden and sat in it is drawn above; the others aren't drawn.
    private fun moreTodayBar(report: CheckReport) {
        val layout = TodayLayout()
            .withPlacement(TodayItem.Timers, TodayPlacement.Fold)
            .withPlacement(TodayItem.WorkUse, TodayPlacement.Fold)
            .withPlacement(TodayItem.Goals, TodayPlacement.Above)
        report.check(
            "td1/more-today: bar shown leaves places as they are",
            layout.placementWith(TodayItem.Timers, true) == TodayPlacement.Fold && layout.placementWith(TodayItem.WorkUse, true) == TodayPlacement.Fold,
        )
        report.check("td1/more-today: bar hidden = no chip for a hideable part", layout.placementWith(TodayItem.Timers, false) == TodayPlacement.Hidden)
        report.check("td1/more-today: bar hidden draws Need it for work above", layout.placementWith(TodayItem.WorkUse, false) == TodayPlacement.Above)
        report.check(
            "td1/more-today: bar hidden moves nothing else",
            layout.placementWith(TodayItem.Goals, false) == TodayPlacement.Above && layout.placementWith(TodayItem.Guard, false) == TodayPlacement.Above,
        )
        report.check("td1/more-today: bar hidden leaves no part in the fold", TodayItem.entries.none { layout.placementWith(it, false) == TodayPlacement.Fold })
    }

    // TD1 3: no steps part; a saved layout naming "daily-steps" reads fine.
    private fun stepsPartGone(report: CheckReport) {
        report.check("td1/steps: no Daily steps part is left", TodayItem.entries.none { it.key == StepsMovedRules.OLD_PART_KEY })
        val saved = TodayLayout.parse("daily-steps,ring,goals", "daily-steps:h,goals:f,ring:f")
        report.check(
            "td1/steps: an old saved layout naming daily-steps reads without it",
            saved.placementOf(TodayItem.Goals) == TodayPlacement.Fold && saved.orderFor(false).containsAll(TodayItem.entries),
        )
        val onlyOld = TodayLayout.parse("daily-steps", "daily-steps:f")
        report.check("td1/steps: a layout of only the old part reads as the default order", onlyOld.orderFor(false) == TodayLayout().orderFor(false))
    }

    // TD1 3: the one-time card.
    private fun stepsMovedCard(report: CheckReport) {
        report.check(
            "td1/steps-card: steps on Today (the default) -> to show",
            StepsMovedRules.initial(null) == StepsMovedNote.Show && StepsMovedRules.initial("goals:f") == StepsMovedNote.Show,
        )
        report.check("td1/steps-card: steps hidden on Today -> finished", StepsMovedRules.initial("goals:f,daily-steps:h") == StepsMovedNote.Done)
        report.check("td1/steps-card: steps only in the fold still counts as on Today", StepsMovedRules.initial("daily-steps:f") == StepsMovedNote.Show)
        fun shows(note: StepsMovedNote, track: Boolean, goal: Boolean, session: Boolean = false) = StepsMovedRules.shows(note, track, goal, session)
        report.check("td1/steps-card: shows when to show, tracking on, no steps goal, idle", shows(StepsMovedNote.Show, true, false))
        report.check("td1/steps-card: not when tracking is off", !shows(StepsMovedNote.Show, false, false))
        report.check("td1/steps-card: not when a steps goal exists", !shows(StepsMovedNote.Show, true, true))
        report.check("td1/steps-card: not in a session", !shows(StepsMovedNote.Show, true, false, session = true))
        report.check("td1/steps-card: not once finished or not checked", !shows(StepsMovedNote.Done, true, false) && !shows(StepsMovedNote.NotChecked, true, false))
        report.check(
            "td1/steps-card: codes round-trip, unknown reads as not checked",
            StepsMovedNote.entries.all { StepsMovedNote.fromCode(it.code) == it } && StepsMovedNote.fromCode(9) == StepsMovedNote.NotChecked,
        )

        val area = GoalArea(UUID.randomUUID(), "Work", "work", "#457B9D", 0.0, now)
        val created = GoalStepsRules.newGoal(UUID.randomUUID(), area.id, 2.0, now)
        report.check(
            "td1/steps-card: the goal is 8,000 steps a day, from daily steps, Show on Today on",
            created.target.amount == 8000.0 && created.target.unit == GoalUnit.Steps && created.target.period == GoalPeriod.Day &&
                created.kind == GoalKind.Amount && created.showOnToday && created.sources.single().type == GoalSourceType.HealthSteps,
        )
        val lists = GoalLists(areas = listOf(area), goals = listOf(created))
        report.check("td1/steps-card: a steps goal is found, then the card stops", GoalStepsRules.hasStepsGoal(lists.liveGoals) && !GoalStepsRules.hasStepsGoal(emptyList()))
        report.check("td1/steps-card: a deleted steps goal does not count", !GoalStepsRules.hasStepsGoal(listOf(created.copy(deletedAt = now))))
        report.check(
            "td1/steps-card: a goal that counts steps by source counts",
            GoalStepsRules.hasStepsGoal(listOf(created.copy(target = GoalTarget(GoalPeriod.Day, 5.0, GoalUnit.Count)))),
        )
        report.check("td1/steps-card: the first live area is used, none = null", GoalStepsRules.areaFor(lists)?.id == area.id && GoalStepsRules.areaFor(GoalLists()) == null)
    }
}
