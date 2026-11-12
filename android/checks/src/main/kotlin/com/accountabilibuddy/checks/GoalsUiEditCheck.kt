package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalAreaRules
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyRules
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalDraftText
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalPhoneRules
import com.accountabilibuddy.shared.goals.GoalReminderDefaults
import com.accountabilibuddy.shared.goals.GoalReviewSettings
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * Round 2 R6b2: the phone's New goal / Edit goal rules ([GoalDrafts]), Edit area and Areas rules ([GoalAreaRules]), Deep work's weekly
 * target and the looser-change wait ([GoalDeepWorkWeeklyRules]) and the Goals settings defaults, checked on their own. Each line names
 * the PC rule it follows (`GoalEditorViewModel`, `GoalTargetEditorViewModel`, `GoalAreaEditorViewModel`, `GoalAreaOrder`, `GoalsService`).
 * The weekly rule is also run against the PC's `GoalLowering` fixtures ([GoalWeeklyParityCheck]).
 */
object GoalsUiEditCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 14, 4, 0, 0, offset)
    private val delay: Duration = Duration.ofMinutes(10)
    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    fun run(report: CheckReport) {
        val name = "goals-ui/edit"
        step1(report, name)
        step2(report, name)
        build(report, name)
        deepWork(report, name)
        areas(report, name)
        weekly(report, name)
        settings(report, name)
    }

    private fun draft(block: (com.accountabilibuddy.shared.goals.GoalDraft) -> com.accountabilibuddy.shared.goals.GoalDraft = { it }) =
        block(GoalDrafts.newDraft(id(1), id(2)).copy(name = "Read"))

    private fun step1(report: CheckReport, name: String) {
        report.check("$name: step 1 needs a name", GoalDrafts.basicsProblem(draft { it.copy(name = "  ") }) == "Give the goal a name.")
        report.check("$name: step 1 caps the name at 80 characters", GoalDrafts.basicsProblem(draft { it.copy(name = "x".repeat(81)) }) != null && GoalDrafts.basicsProblem(draft { it.copy(name = "x".repeat(80)) }) == null)
        report.check("$name: step 1 needs an area", GoalDrafts.basicsProblem(draft { it.copy(areaId = null) }) == "Pick an area (or make one).")
        report.check("$name: step 1 is clear with a name and an area, and a new goal starts as a daily habit ticked by hand", GoalDrafts.basicsProblem(draft()) == null &&
            draft().kind == GoalKind.Habit && draft().period == GoalPeriod.Day && draft().sources.map { it.type } == listOf(GoalSourceType.Hand) && draft().icon == "track_changes" && draft().color == "#0D9488")
        val weeklyHabit = GoalDrafts.withKind(draft { it.copy(kind = GoalKind.Time, period = GoalPeriod.Week) }, GoalKind.Habit)
        report.check("$name: a non-daily habit starts at 3 times a period (the PC's SetKind)", weeklyHabit.amount == "3" && GoalDrafts.withKind(draft(), GoalKind.Time).kind == GoalKind.Time)
        report.check("$name: the icon and colour lists are the PC's (20 icons all known, 24 colours, the first 8 its palette)", GoalDrafts.ICONS.size == 20 && GoalDrafts.COLORS.size == 24 && GoalDrafts.COLORS[5] == GoalDrafts.DEFAULT_COLOR)
    }

    private fun step2(report: CheckReport, name: String) {
        fun problem(d: com.accountabilibuddy.shared.goals.GoalDraft) = GoalDrafts.targetProblem(d)
        report.check("$name: a daily habit needs nothing more (by hand), or a threshold of 1 to 1,440 min", problem(draft()) == null && problem(draft { it.copy(threshold = "15") }) == null &&
            problem(draft { it.copy(threshold = "0") }) == "Ticks itself at 1 to 1,440 min." && problem(draft { it.copy(threshold = "1441") }) != null)
        report.check("$name: a weekly habit needs 1 to 31 times", problem(draft { it.copy(period = GoalPeriod.Week, amount = "3") }) == null &&
            problem(draft { it.copy(period = GoalPeriod.Week, amount = "") }) == "How many times? 1 to 31." && problem(draft { it.copy(period = GoalPeriod.Month, amount = "32") }) != null)
        val time = draft { it.copy(kind = GoalKind.Time) }
        report.check("$name: a time goal needs an amount or a long-term target", problem(time) == "Set an amount for each period, a long-term target, or both." &&
            problem(time.copy(amount = "30")) == null && problem(time.copy(longTerm = "50")) == null)
        report.check("$name: the amount is more than 0, the long-term target too, and a date needs a long-term amount",
            problem(time.copy(amount = "0")) == "The amount is more than 0." && problem(time.copy(amount = "30", longTerm = "0")) == "The long-term target is more than 0." &&
                problem(time.copy(amount = "30", targetDate = LocalDate.of(2027, 1, 31))) == "A date needs a long-term amount to reach.")
        report.check("$name: on N days must fit the period (7 a week, 31 a month)", problem(time.copy(period = GoalPeriod.Week, amount = "30", weeklyDays = "5")) == null &&
            problem(time.copy(period = GoalPeriod.Week, amount = "30", weeklyDays = "8")) == "The number of days doesn't fit the period." && problem(time.copy(period = GoalPeriod.Month, amount = "30", weeklyDays = "20")) == null &&
            problem(time.copy(period = GoalPeriod.Week, amount = "30", weeklyDays = "0")) != null)
        report.check("$name: text that isn't a number is a problem, comma decimals read", problem(time.copy(amount = "abc")) == "Numbers only, please." && GoalDrafts.number("1,5") == 1.5 && GoalDrafts.number(" ") == null)
        report.check("$name: a milestone needs nothing (amount optional)", problem(draft { it.copy(kind = GoalKind.Milestone) }) == null)
        val label = GoalSourceRule(id(10), GoalSourceType.FocusLabel)
        report.check("$name: a label source needs its label; all focus and Coming soon sources can't be added to an ordinary goal",
            GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(label)) }) == "Pick a label for the focus source." &&
                GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(label.copy(labelId = id(11)))) }) == null &&
                GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(GoalSourceRule(id(12), GoalSourceType.FocusAll))) }) != null &&
                GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(GoalSourceRule(id(13), GoalSourceType.GoalField))) }) != null &&
                GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(GoalSourceRule(id(14), GoalSourceType.MilestoneTasks))) }) != null)
        report.check("$name: sleep and steps sources can be saved; PC-only sources are marked Measured on your PC, Coming soon ones aren't counted on the phone",
            GoalDrafts.sourcesProblem(draft { it.copy(sources = listOf(GoalSourceRule(id(15), GoalSourceType.HealthSleep), GoalSourceRule(id(16), GoalSourceType.HealthSteps))) }) == null &&
                GoalPhoneRules.measuredOnPc(GoalSourceType.FocusTag) && GoalPhoneRules.measuredOnPc(GoalSourceType.CalendarCategory) && !GoalPhoneRules.countsOnPhone(GoalSourceType.GoalField))
        report.check("$name: Create stays off until step 1, the target and the sources are all clear", GoalDrafts.canCreate(draft()) && !GoalDrafts.canCreate(draft { it.copy(name = "") }) && !GoalDrafts.canCreate(time))
    }

    private fun build(report: CheckReport, name: String) {
        val habit = GoalDrafts.buildTarget(draft { it.copy(threshold = "15") })
        report.check("$name: a daily habit's target is a tick (no amount), its threshold goes on its time sources only",
            habit == GoalTarget(GoalPeriod.Day, null, GoalUnit.YesNo) && GoalDrafts.threshold(draft { it.copy(threshold = "15") }) == 15.0)
        val label = GoalSourceRule(id(10), GoalSourceType.FocusLabel, labelId = id(11))
        val sources = GoalDrafts.buildSources(draft { it.copy(threshold = "15", sources = listOf(label, GoalSourceRule(id(17), GoalSourceType.Hand))) })
        report.check("$name: the threshold lands on the time source and not the hand source", sources[0].thresholdMinutes == 15.0 && sources[1].thresholdMinutes == null)
        val timeGoal = GoalDrafts.buildTarget(draft { it.copy(kind = GoalKind.Time, period = GoalPeriod.Week, amount = "300", weeklyDays = "5", longTerm = "50", targetDate = LocalDate.of(2027, 1, 31)) })
        report.check("$name: a time goal's long-term hours become minutes, units and days as the PC builds them",
            timeGoal == GoalTarget(GoalPeriod.Week, 300.0, GoalUnit.Minutes, GoalDirection.AtLeast, 3000.0, LocalDate.of(2027, 1, 31), 5))
        val weight = GoalDrafts.buildTarget(draft { it.copy(kind = GoalKind.Amount, unit = GoalUnit.Lb, direction = GoalDirection.Reach, longTerm = "175", targetDate = LocalDate.of(2027, 1, 31)) })
        report.check("$name: a weight to reach has no per-period amount, its long-term amount and date", weight == GoalTarget(GoalPeriod.Day, null, GoalUnit.Lb, GoalDirection.Reach, 175.0, LocalDate.of(2027, 1, 31), null))
        report.check("$name: a date without a long-term amount is dropped from the target (never built: it is a problem first)", GoalDrafts.buildTarget(draft { it.copy(kind = GoalKind.Time, amount = "30", targetDate = LocalDate.of(2027, 1, 31)) }) == null)

        val made = GoalDrafts.toGoal(draft { it.copy(name = "  Read  ", threshold = "10") }, null, now, 4.0)
        report.check("$name: a new goal is made trimmed, created now, at the end of its area, with no pause or second target",
            made != null && made.name == "Read" && made.createdAt == now && made.position == 4.0 && made.pauses == null && made.secondTarget == null && !made.builtIn && made.areaId == id(2))
        val base = made!!.copy(createdAt = now.minusDays(40), position = 2.0, pauses = null)
        val edited = GoalDrafts.toGoal(GoalDrafts.fromGoal(base).copy(name = "Read more", period = GoalPeriod.Week, amount = "4"), base, now, 9.0)
        report.check("$name: an edit keeps its age and place, changes its target at once (no waiting change kept for an ordinary goal)",
            edited != null && edited.createdAt == base.createdAt && edited.position == 2.0 && edited.target.period == GoalPeriod.Week && edited.target.amount == 4.0 && edited.target.pendingChange == null && edited.name == "Read more")
        val moved = GoalDrafts.toGoal(GoalDrafts.fromGoal(base).copy(areaId = id(3)), base, now, 9.0)
        report.check("$name: moving to another area puts it at the end of that area", moved?.position == 9.0 && moved.areaId == id(3))
        val round = GoalDrafts.fromGoal(base)
        report.check("$name: a goal read into a draft and made again is the same goal", GoalDrafts.toGoal(round, base, now, 2.0) == base)
        val lowered = GoalDrafts.toGoal(GoalDrafts.fromGoal(edited!!).copy(amount = "1"), edited, now, 2.0)
        report.check("$name: lowering an ordinary goal's target applies at once on the phone, as on the PC (only Deep work waits)", lowered?.target?.amount == 1.0 && lowered.target.pendingChange == null)
        report.check("$name: a draft with a problem makes no goal", GoalDrafts.toGoal(draft { it.copy(name = "") }, null, now, 1.0) == null && GoalDrafts.toGoal(draft { it.copy(kind = GoalKind.Time) }, null, now, 1.0) == null)
        val halfMade = draft { it.copy(kind = GoalKind.Time, amount = "30", longTerm = "5", targetDate = LocalDate.of(2027, 1, 31), sources = listOf(GoalSourceRule(id(10), GoalSourceType.FocusLabel, labelId = id(11)))) }
        report.check("$name: a half-made goal survives a screen turn (text round trip)", GoalDraftText.read(GoalDraftText.write(halfMade)) == halfMade && GoalDraftText.read("not json") == null)
    }

    private fun deepWork(report: CheckReport, name: String) {
        val deep = GoalDefaults.newDeepWork(GoalDefaults.WORK_AREA_ID, now)
        val d = GoalDrafts.fromGoal(deep, 600)
        report.check("$name: Deep work's draft reads its weekly target in hours and is flagged", d.isDeepWork && d.weeklyHours == "10")
        report.check("$name: Deep work's daily target is read only on the phone (ADR 29), nothing but the weekly target is built from the draft",
            !GoalPhoneRules.canEditDailyTarget(deep) && GoalDrafts.buildTarget(d) == null && GoalDrafts.deepWorkWeeklyMinutes(d) == 600 && GoalDrafts.deepWorkWeeklyMinutes(d.copy(weeklyHours = "")) == null)
        report.check("$name: Deep work's weekly target must be 30 min to 56h", GoalDrafts.targetProblem(d.copy(weeklyHours = "0.25")) == "The weekly target is 30 min to 56h." && GoalDrafts.targetProblem(d.copy(weeklyHours = "57")) != null && GoalDrafts.targetProblem(d.copy(weeklyHours = "56")) == null && GoalDrafts.targetProblem(d) == null)
        val kept = GoalDrafts.toGoal(d.copy(name = "Focus", icon = "timer"), deep, now, 5.0)
        report.check("$name: editing Deep work takes name, icon, colour and area only; its kind, sources and built-in mark stay",
            kept != null && kept.name == "Focus" && kept.icon == "timer" && kept.builtIn && kept.kind == GoalKind.Time && kept.sources == deep.sources && kept.target == deep.target)
        report.check("$name: a draft can't turn Deep work into another kind", GoalDrafts.withKind(d, GoalKind.Habit).kind == GoalKind.Time)
    }

    private fun areas(report: CheckReport, name: String) {
        fun area(n: Int, position: Double, dead: Boolean = false) = GoalArea(id(100 + n), "Area $n", "work", "#2563EB", position, now, if (dead) now else null)
        report.check("$name: an area needs a name of up to 80 characters", GoalAreaRules.nameProblem(" ") == "Give the area a name." && GoalAreaRules.nameProblem("x".repeat(81)) != null && GoalAreaRules.nameProblem("Learning") == null)
        val list = listOf(area(1, 1.0), area(2, 2.0), area(3, 3.0), area(4, 4.0, dead = true))
        report.check("$name: a new area goes after the last live one", GoalAreaRules.nextPosition(list) == 4.0)
        val up = GoalAreaRules.moved(list, id(103), 0)
        report.check("$name: moving an area up renumbers 1..N and saves only the areas that moved", up.map { it.id to it.position }.toSet() == setOf(id(103) to 1.0, id(101) to 2.0, id(102) to 3.0))
        report.check("$name: moving to the same place or past the ends changes nothing / clamps", GoalAreaRules.moved(list, id(102), 1).isEmpty() && GoalAreaRules.moved(list, id(101), 99).map { it.id }.toSet() == setOf(id(101), id(102), id(103)) && GoalAreaRules.moved(list, id(104), 0).isEmpty())
        val goals = listOf(
            Goal(id(200), id(101), "g", "work", "#fff", GoalKind.Habit, GoalTarget(GoalPeriod.Day, null, GoalUnit.YesNo), emptyList(), now, 1.0, now),
            Goal(id(201), id(102), "g", "work", "#fff", GoalKind.Habit, GoalTarget(GoalPeriod.Day, null, GoalUnit.YesNo), emptyList(), now, 1.0, now, deletedAt = now),
        )
        report.check("$name: an area with a live goal can't be deleted (it says why); an empty one, or one with only deleted goals, can", GoalAreaRules.deleteProblem(goals, id(101)) != null &&
            GoalAreaRules.deleteProblem(goals, id(102)) == null && GoalAreaRules.deleteProblem(goals, id(103)) == null && GoalAreaRules.goalCount(goals, id(101)) == 1)
        report.check("$name: the area icon list is the PC's 15 names", GoalAreaRules.ICONS.size == 15 && GoalAreaRules.ICONS.first() == "work")
    }

    private fun weekly(report: CheckReport, name: String) {
        fun set(state: GoalDeepWorkWeekly, to: Int?, pb: Boolean = false, session: Boolean = false, held: Boolean = false, at: OffsetDateTime = now) =
            GoalDeepWorkWeeklyRules.set(state, to, pb, session, held, at, delay)
        val none = GoalDeepWorkWeekly()
        val ten = GoalDeepWorkWeekly(600)
        report.check("$name: a first weekly target applies at once, also during a session", set(none, 600).state.minutes == 600 && set(none, 600, session = true).result == GoalDeepWorkWeeklyResult.Applied)
        report.check("$name: raising applies at once, also during a session, and drops a waiting change", set(ten.copy(pending = GoalPendingChange(300.0, now.plusMinutes(5))), 900, session = true).let { it.result == GoalDeepWorkWeeklyResult.Applied && it.state == GoalDeepWorkWeekly(900) })
        val lower = set(ten, 480)
        report.check("$name: lowering waits the looser-change delay (10 min), the target stays", lower.result == GoalDeepWorkWeeklyResult.Waiting && lower.state.minutes == 600 && lower.state.pending == GoalPendingChange(480.0, now.plusMinutes(10)))
        report.check("$name: removing the target waits too", set(ten, null).state.pending == GoalPendingChange(null, now.plusMinutes(10)))
        report.check("$name: lowering during a session outside ProductaBuddy mode is refused and keeps nothing", set(ten, 480, session = true).let { it.result == GoalDeepWorkWeeklyResult.RefusedDuringSession && it.state == ten })
        report.check("$name: in ProductaBuddy mode lowering applies at once, in a session too, unless the work-hours rule holds it (then it waits with no delay, marked)",
            set(ten, 480, pb = true, session = true).state == GoalDeepWorkWeekly(480) &&
                set(ten, 480, pb = true, held = true).state.pending == GoalPendingChange(480.0, now, true))
        report.check("$name: asking again for the same lower amount keeps the first wait's time", set(lower.state, 480, at = now.plusMinutes(3)).state.pending?.appliesAt == now.plusMinutes(10))
        report.check("$name: asking for another lower amount starts that wait", set(lower.state, 420, at = now.plusMinutes(3)).state.pending == GoalPendingChange(420.0, now.plusMinutes(13)))
        report.check("$name: asking for the same target drops a waiting change", set(lower.state, 600).let { it.result == GoalDeepWorkWeeklyResult.Unchanged && it.state == ten })
        report.check("$name: the amount is clamped to 30 min to 56h", set(none, 5).state.minutes == 30 && set(none, 99999).state.minutes == 3360)

        fun settle(state: GoalDeepWorkWeekly, at: OffsetDateTime, pb: Boolean = false, session: Boolean = false, held: Boolean = false) =
            GoalDeepWorkWeeklyRules.settle(state, pb, session, held, at, delay)
        val waiting = lower.state
        report.check("$name: a wait doesn't apply before its time", settle(waiting, now.plusMinutes(9)) == waiting)
        report.check("$name: a wait applies at its time when no session runs", settle(waiting, now.plusMinutes(10)) == GoalDeepWorkWeekly(480))
        report.check("$name: a wait never applies while a session runs (outside ProductaBuddy mode), and is still there after", settle(waiting, now.plusHours(3), session = true) == waiting && settle(waiting, now.plusHours(3)).minutes == 480)
        report.check("$name: the work-hours rule holds a due wait", settle(waiting, now.plusHours(1), held = true) == waiting)
        val made = set(ten, 480, pb = true, held = true).state
        report.check("$name: a wait made in ProductaBuddy mode restarts its full delay once AccountabiliBuddy mode sees it",
            settle(made, now.plusHours(5)).let { it.minutes == 600 && it.pending == GoalPendingChange(480.0, now.plusHours(5).plusMinutes(10)) } &&
                settle(settle(made, now.plusHours(5)), now.plusHours(5).plusMinutes(10)) == GoalDeepWorkWeekly(480))
        report.check("$name: Cancel drops the waiting change and leaves the target", GoalDeepWorkWeeklyRules.cancel(waiting) == ten)
        report.check("$name: there is no apply-now: nothing in the rule moves a wait earlier than its time", settle(waiting, now.plusMinutes(1), pb = false).pending?.appliesAt == now.plusMinutes(10))

        // §3b 6: a backup restore goes through the same rules.
        report.check("$name: a backup with a lower weekly target waits like any looser change", GoalDeepWorkWeeklyRules.restore(ten, 300, false, false, false, now, delay).let { it.result == GoalDeepWorkWeeklyResult.Waiting && it.state.pending?.amount == 300.0 && it.state.minutes == 600 })
        report.check("$name: a backup with no weekly target (a removal) waits too", GoalDeepWorkWeeklyRules.restore(ten, null, false, false, false, now, delay).state.pending == GoalPendingChange(null, now.plusMinutes(10)))
        report.check("$name: a backup's lower weekly target is refused during a session", GoalDeepWorkWeeklyRules.restore(ten, 300, false, true, false, now, delay).let { it.result == GoalDeepWorkWeeklyResult.RefusedDuringSession && it.state == ten })
        report.check("$name: a backup's higher weekly target applies at once, in a session too", GoalDeepWorkWeeklyRules.restore(ten, 900, false, true, false, now, delay).state == GoalDeepWorkWeekly(900))
        report.check("$name: a weekly target is a view for the goals maths (week period, minutes)", ten.asTarget() == GoalTarget(GoalPeriod.Week, 600.0, GoalUnit.Minutes) && none.asTarget() == null)
    }

    private fun settings(report: CheckReport, name: String) {
        report.check("$name: review defaults are the week's first day at 6:00 PM, snooze 1 day, no tidy-up after (the PC's)", GoalReviewSettings().let { it.day == null && it.time == LocalTime.of(18, 0) && it.snoozeDays == 1 && !it.tidyUpAfter })
        report.check("$name: a snooze is 1 or 2 days and a time has no seconds", GoalReviewSettings(snoozeDays = 7).clamped().snoozeDays == 2 && GoalReviewSettings(snoozeDays = 0).clamped().snoozeDays == 1 && GoalReviewSettings(time = LocalTime.of(8, 30, 45)).clamped().time == LocalTime.of(8, 30))
        report.check("$name: reminder defaults: skip when done on, nudge on in AccountabiliBuddy mode and off in ProductaBuddy mode", GoalReminderDefaults().let { it.skipWhenDone && it.nudgeFor(false) && !it.nudgeFor(true) })
    }
}
