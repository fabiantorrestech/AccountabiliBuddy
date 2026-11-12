package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReminderDays
import com.accountabilibuddy.shared.goals.GoalReminderDefaults
import com.accountabilibuddy.shared.goals.GoalReminderEdits
import com.accountabilibuddy.shared.goals.GoalReminderMark
import com.accountabilibuddy.shared.goals.GoalReminderMarkKind
import com.accountabilibuddy.shared.goals.GoalReminderPlan
import com.accountabilibuddy.shared.goals.GoalReminderState
import com.accountabilibuddy.shared.goals.GoalReviewAdjustment
import com.accountabilibuddy.shared.goals.GoalReviewChoice
import com.accountabilibuddy.shared.goals.GoalReviewRecord
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSettings
import com.accountabilibuddy.shared.goals.GoalReviewSnooze
import com.accountabilibuddy.shared.goals.GoalReviewWeek
import com.accountabilibuddy.shared.goals.GoalSleepReadings
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.goals.GoalWeekStart
import com.accountabilibuddy.shared.goals.GoalWeekday
import com.accountabilibuddy.shared.goals.GoalWindow
import java.io.File
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * Round 2 R6b3's phone rules, checked on their own where the PC's twin isn't a pure function (its review service and reminder source
 * run on a store; their pure parts have parity fixtures: review-due, reminder-times, nudge, adjust, reminder-done):
 * - the weekly review: due and shown, never in a session, snooze per mode (AB asks again, PB stays snoozed), answers kept and Stop for
 *   now resuming at the next unanswered goal, Deep work's Pause refused, Adjust pre-filling Edit goal › Target, the week boxes;
 * - goal reminders: ring at their time, skip when done, a paused goal silent, the nudge once, held during a session and rung after it,
 *   nothing before the data is in, rung once (a shown mark), the editor's lines, "Next: today 8:00 PM", per-mode defaults;
 * - sleep readings; the Deep work weekly drop (carry item); trusted time only (no clock read in the rules or the ringing code).
 */
object GoalsUiReviewRemindersCheck {
    private val zone = ZoneOffset.ofHours(-5)
    private val today = LocalDate.of(2026, 11, 11) // a Wednesday
    private fun at(day: LocalDate, hour: Int, minute: Int = 0): OffsetDateTime = OffsetDateTime.of(day.atTime(hour, minute), zone)
    private val made = at(today.minusDays(60), 8)
    private val areaA = id(1)
    private val areaB = id(2)

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun goal(n: Int, kind: GoalKind = GoalKind.Habit, target: GoalTarget = GoalTarget(GoalPeriod.Day, null, GoalUnit.YesNo), area: UUID = areaA, position: Double = n.toDouble(), pauses: List<GoalPauseStretch>? = null, sources: List<GoalSourceRule> = listOf(GoalSourceRule(id(900 + n), GoalSourceType.Hand))) =
        Goal(id(n), area, "Goal $n", "school", "#2E7D6B", kind, target, sources, made, position, made, pauses = pauses)

    private fun tick(goal: Goal, day: LocalDate, n: Int) = GoalEntry(id(5000 + n), goal.id, day, null, 1.0, GoalEntrySource.Hand, at(day, 21))

    private fun reminder(n: Int, goal: Goal, hour: Int, edited: OffsetDateTime = made, skip: Boolean = true, nudge: Boolean = false) =
        GoalReminder(id(700 + n), goal.id, GoalReminderDays.EveryDay, LocalTime.of(hour, 0), n.toDouble(), edited, skipWhenDone = skip, nudgeWhenBehind = nudge)

    fun run(report: CheckReport) {
        review(report, "goals-ui/review")
        reminders(report, "goals-ui/reminders")
        editor(report, "goals-ui/reminder-editor")
        sleep(report, "goals-ui/sleep")
        carry(report, "goals-ui/carry")
        trusted(report, "goals-ui/trusted-time")
    }

    private fun review(report: CheckReport, name: String) {
        val areas = listOf(GoalArea(areaA, "A", "work", "#000000", 2.0, made), GoalArea(areaB, "B", "work", "#000000", 1.0, made))
        val g1 = goal(11, area = areaA, position = 1.0)
        val g2 = goal(12, area = areaB, position = 5.0)
        val paused = goal(13, area = areaB, position = 1.0, pauses = listOf(GoalPauseStretch(today.minusDays(2))))
        val deep = GoalDefaults.newDeepWork(areaA, made).copy(position = 0.0)
        val asked = GoalReviewRules.goalsToReview(listOf(g1, g2, paused, deep), areas, today)
        report.check("$name: asks every live goal not paused today (Deep work too), areas in their order then goals", asked.map { it.id } == listOf(g2.id, deep.id, g1.id), "${asked.map { it.name }}")

        // Settings: Sunday 6:00 PM; now Wed Nov 11 7:00 PM -> due Sun Nov 8, for the week of Sun Nov 1 (the week before).
        val now = at(today, 19).toInstant()
        val day = GoalReviewRules.reviewDay(GoalReviewSettings(day = DayOfWeek.SUNDAY), GoalWeekStart.Sunday)
        val due = GoalReviewRules.dueAt(now, day, LocalTime.of(18, 0), zone)
        val week = GoalReviewRules.weekOf(due, GoalWeekStart.Sunday, zone)
        report.check("$name: due at the day and time, for the week before (Sun Nov 8 6:00 PM: the week of Nov 1)", due == at(LocalDate.of(2026, 11, 8), 18) && week == LocalDate.of(2026, 11, 1), "$due $week")
        report.check("$name: the phone's subtitle week text (board: 'Nov 9–15')", GoalReviewRules.weekText(LocalDate.of(2026, 11, 9), compact = true) == "Nov 9–15" && GoalReviewRules.weekText(LocalDate.of(2026, 10, 29), compact = true) == "Oct 29–Nov 4")
        report.check("$name: no day set: the week's first day", GoalReviewRules.reviewDay(GoalReviewSettings(), GoalWeekStart.Monday) == DayOfWeek.MONDAY)

        val shown = GoalReviewRules.row(true, false, asked, null, now, false, day)
        report.check("$name: the card shows when due ('Weekly goals review · 3 goals', 'About 2 minutes · Sundays')", shown.shown && shown.canSnooze && shown.title == "Weekly goals review · 3 goals" && shown.note == "About 2 minutes · Sundays", "$shown")
        report.check("$name: never during a focus session (card hidden)", !GoalReviewRules.row(true, true, asked, null, now, false, day).shown)
        report.check("$name: not when the goals can't be used", !GoalReviewRules.row(false, false, asked, null, now, false, day).shown)

        val record0 = GoalReviewRecord(id(800), week, emptyList(), 1.0, at(today, 19))
        val abUntil = GoalReviewRules.snoozeUntil(GoalReviewSnooze.Tomorrow, false, due, now, zone)
        val ab2 = GoalReviewRules.snoozeUntil(GoalReviewSnooze.TwoDays, false, due, now, zone)
        val pbUntil = GoalReviewRules.snoozeUntil(GoalReviewSnooze.Tomorrow, true, due, now, zone)
        report.check("$name: AB snooze: tomorrow or in 2 days at 9:00 AM ('Thu, Nov 12 · 9:00 AM')", abUntil == at(today.plusDays(1), 9) && ab2 == at(today.plusDays(2), 9) && GoalReviewRules.snoozeLine(abUntil, zone) == "Thu, Nov 12 · 9:00 AM", "$abUntil $ab2")
        report.check("$name: PB snooze: until next week's review", pbUntil == due.plusDays(7))
        val abSnoozed = record0.copy(snoozedUntil = abUntil)
        report.check("$name: AB: snoozed hides the card, then it asks again when the snooze ends",
            !GoalReviewRules.row(true, false, asked, abSnoozed, now, false, day).shown && GoalReviewRules.row(true, false, asked, abSnoozed, abUntil.toInstant().plusSeconds(1), false, day).shown)
        val pbRow = GoalReviewRules.row(true, false, asked, record0.copy(snoozedUntil = pbUntil), at(today.plusDays(3), 9).toInstant(), true, day)
        report.check("$name: PB: a snooze stays snoozed (quiet card, still startable, no Snooze)", pbRow.shown && pbRow.snoozed && !pbRow.canSnooze && pbRow.note == "Snoozed · start it whenever you like", "$pbRow")
        report.check("$name: the notification rings at the due moment; AB again at the snooze's end; PB not again",
            GoalReviewRules.ringAt(due, null, false) == due.toInstant() && GoalReviewRules.ringAt(due, abSnoozed, false) == abUntil.toInstant()
                && GoalReviewRules.ringAt(due, record0.copy(snoozedUntil = pbUntil), true) == null)

        val answered = GoalReviewRules.withAnswer(record0, asked[0].id, GoalReviewChoice.KeepGoing)
        val again = GoalReviewRules.withAnswer(answered, asked[0].id, GoalReviewChoice.DecideNextWeek)
        report.check("$name: an answer is kept in the week's record; a later answer for the same goal replaces it", again.decisions.size == 1 && GoalReviewRules.answerOf(again, asked[0].id) == GoalReviewChoice.DecideNextWeek)
        report.check("$name: Stop for now resumes at the next unanswered goal", GoalReviewRules.firstUnanswered(answered, asked, 0) == 1 && GoalReviewRules.firstUnanswered(null, asked, 0) == 0)
        val done = asked.fold(record0) { record, g -> GoalReviewRules.withAnswer(record, g.id, GoalReviewChoice.KeepGoing) }
        report.check("$name: every goal answered: the card goes", GoalReviewRules.isComplete(done, asked) && !GoalReviewRules.row(true, false, asked, done, now, false, day).shown && GoalReviewRules.firstUnanswered(done, asked, 0) == null)
        report.check("$name: Pause this goal is refused for Deep work, allowed for the others",
            GoalReviewRules.refusal(deep, GoalReviewChoice.Pause) == "Deep work can't be paused" && GoalReviewRules.refusal(g1, GoalReviewChoice.Pause) == null && GoalReviewRules.refusal(deep, GoalReviewChoice.KeepGoing) == null)
        report.check("$name: the four choices are the brief's", GoalReviewChoice.entries.map(GoalReviewRules::choiceTitle) == listOf("Keep going", "Adjust the target", "Pause this goal", "Decide next week"))
        report.check("$name: the newest live record of the week is the one read", GoalReviewRules.recordFor(listOf(record0, answered.copy(editedAt = at(today, 20)), record0.copy(id = id(801), week = week.minusDays(7))), week) == answered.copy(editedAt = at(today, 20)))

        // Adjust pre-fills Edit goal > Target.
        val weekly = goal(14, GoalKind.Time, GoalTarget(GoalPeriod.Week, 300.0, GoalUnit.Minutes))
        val draft = GoalDrafts.fromGoal(weekly)
        val filled = GoalDrafts.withAdjustment(draft, GoalReviewAdjustment(null, 215.0, null))
        val dated = GoalDrafts.withAdjustment(draft, GoalReviewAdjustment(LocalDate.of(2027, 3, 3), null, null))
        val habitDays = GoalDrafts.withAdjustment(GoalDrafts.fromGoal(goal(15, target = GoalTarget(GoalPeriod.Week, 3.0, GoalUnit.YesNo))), GoalReviewAdjustment(null, null, 2))
        report.check("$name: Adjust pre-fills the amount, the date, a habit's times a week; the target is built from it",
            filled.amount == "215" && GoalDrafts.buildTarget(filled)?.amount == 215.0 && dated.targetDate == LocalDate.of(2027, 3, 3) && habitDays.amount == "2", "${filled.amount} ${habitDays.amount}")
        report.check("$name: Adjust leaves Deep work's draft alone (its daily target is the PC's) and does nothing without numbers",
            GoalDrafts.withAdjustment(draft.copy(isDeepWork = true), GoalReviewAdjustment(null, 60.0, null)).amount == draft.amount && GoalDrafts.withAdjustment(draft, null) == draft)

        // The week boxes: 3 times a week, ticked Mon and Wed of Nov 1-7.
        val run = goal(16, target = GoalTarget(GoalPeriod.Week, 3.0, GoalUnit.YesNo))
        val data = GoalSourceData(emptyList(), listOf(tick(run, LocalDate.of(2026, 11, 2), 1), tick(run, LocalDate.of(2026, 11, 4), 2)), zone)
        val boxes = GoalReviewWeek.of(run, data, week, today)
        report.check("$name: the week's seven boxes with letters, ticked when hit, '2 of 3 · one more would have made it'",
            boxes != null && boxes.cells.map { it.letter } == listOf("S", "M", "T", "W", "T", "F", "S") && boxes.cells.map { it.hit } == listOf(false, true, false, true, false, false, false)
                && boxes.bold == "2 of 3" && boxes.rest == "one more would have made it", "$boxes")
        report.check("$name: no week boxes for a goal that isn't a habit", GoalReviewWeek.of(weekly, data, week, today) == null)
    }

    private fun reminders(report: CheckReport, name: String) {
        val floss = goal(21)
        val water = goal(22)
        val pausedGoal = goal(23, pauses = listOf(GoalPauseStretch(today)))
        val reminders = listOf(reminder(1, floss, 20), reminder(2, water, 8), reminder(3, pausedGoal, 8))
        val data = GoalSourceData(emptyList(), listOf(tick(water, today, 1)), zone)
        val goals = listOf(floss, water, pausedGoal)
        fun step(now: OffsetDateTime, state: GoalReminderState = GoalReminderState.EMPTY, held: Boolean = false, d: GoalSourceData? = data, list: List<GoalReminder> = reminders) =
            GoalReminderPlan.step(list, goals, state, d, GoalWeekStart.Sunday, now.toInstant(), zone, held)

        val morning = step(at(today, 9))
        report.check("$name: skip when done is checked when it comes due (water ticked: skipped, not rung)", morning.ring.isEmpty() && morning.skip == listOf(GoalReminderPlan.reminderKey(id(702), at(today, 8).toInstant())), "${morning.ring} ${morning.skip}")
        report.check("$name: a goal paused today sends nothing", morning.ring.none { it.goalId == pausedGoal.id } && morning.skip.none { it.contains(id(703).toString()) })
        report.check("$name: the next one is 8:00 PM (for the alarm)", morning.nextAt == at(today, 20).toInstant(), "${morning.nextAt}")
        val evening = step(at(today, 20, 1))
        report.check("$name: rings at its time", evening.ring.map { it.goalId } == listOf(floss.id) && evening.ring[0].due == at(today, 20))
        report.check("$name: not before its time", step(at(today, 19, 59)).ring.isEmpty())
        val shownState = GoalReminderState.EMPTY.with(evening.ring[0].key, GoalReminderMark(GoalReminderMarkKind.Shown, at(today, 20, 1).toInstant()), at(today, 20, 1).toInstant())
        report.check("$name: rings once (a shown occurrence doesn't ring again)", step(at(today, 20, 30), shownState).ring.isEmpty())
        val held = step(at(today, 20, 1), held = true)
        report.check("$name: held during a focus session (nothing rings, it waits)", held.ring.isEmpty() && held.held.map { it.goalId } == listOf(floss.id))
        report.check("$name: after the session it rings", step(at(today, 21, 15)).ring.map { it.goalId } == listOf(floss.id))
        report.check("$name: nothing comes due before the goal data is in", step(at(today, 20, 1), d = null).ring.isEmpty())
        val snoozed = GoalReminderState.EMPTY.with(evening.ring[0].key, GoalReminderMark(GoalReminderMarkKind.Snoozed, at(today, 20, 1).toInstant(), at(today, 20, 11).toInstant(), 1), at(today, 20, 1).toInstant())
        report.check("$name: a snooze rings again at its end", step(at(today, 20, 5), snoozed).ring.isEmpty() && step(at(today, 20, 12), snoozed).ring.map { it.snoozeCount } == listOf(1))
        report.check("$name: yesterday's unrung occurrence doesn't ring today", step(at(today.plusDays(1), 7)).ring.isEmpty())
        val yesterdaySnooze = GoalReminderState.EMPTY.with(GoalReminderPlan.reminderKey(id(701), at(today, 20).toInstant()), GoalReminderMark(GoalReminderMarkKind.Snoozed, at(today, 23, 50).toInstant(), at(today.plusDays(1), 0, 5).toInstant()), at(today, 23, 50).toInstant())
        report.check("$name: ...unless snoozed into today", step(at(today.plusDays(1), 0, 10), yesterdaySnooze).ring.map { it.goalId } == listOf(floss.id))
        report.check("$name: never for a moment before it was set (set at 8:30 PM: not today)", step(at(today, 21), list = listOf(reminder(1, floss, 20, edited = at(today, 20, 30)))).ring.isEmpty())

        // The nudge: floss ticked Sun and Mon, Tuesday missed -> dropped with yesterday: once at 9:00 AM this morning.
        val nudging = listOf(reminder(4, floss, 20, nudge = true))
        val ticks = GoalSourceData(emptyList(), listOf(tick(floss, LocalDate.of(2026, 11, 8), 2), tick(floss, LocalDate.of(2026, 11, 9), 3)), zone)
        val nudgeNow = GoalReminderPlan.step(nudging, goals, GoalReminderState.EMPTY, ticks, GoalWeekStart.Sunday, at(today, 9, 1).toInstant(), zone, false)
        val nudge = nudgeNow.ring.singleOrNull { it.nudge }
        report.check("$name: the nudge rings once at 9:00 AM the morning after the drop", nudge != null && nudge.due == at(today, 9), "${nudgeNow.ring}")
        val after = GoalReminderState.EMPTY.with(nudge!!.key, GoalReminderMark(GoalReminderMarkKind.Shown, at(today, 9, 1).toInstant()), at(today, 9, 1).toInstant())
        val tomorrow = GoalReminderPlan.step(nudging, goals, after, ticks, GoalWeekStart.Sunday, at(today.plusDays(1), 9, 30).toInstant(), zone, false)
        report.check("$name: ...not again that day, nor the next (still behind, no new drop)",
            GoalReminderPlan.step(nudging, goals, after, ticks, GoalWeekStart.Sunday, at(today, 12).toInstant(), zone, false).ring.none { it.nudge } && tomorrow.ring.none { it.nudge })
        report.check("$name: no nudge without 'Nudge when behind'", step(at(today, 9, 1), d = ticks).ring.none { it.nudge })
        report.check("$name: per-mode defaults: a new reminder nudges in AB, not in PB; skip when done on in both",
            GoalReminderEdits.rulesOf(emptyList(), GoalReminderDefaults(), false) == (true to true) && GoalReminderEdits.rulesOf(emptyList(), GoalReminderDefaults(), true) == (true to false))
    }

    private fun editor(report: CheckReport, name: String) {
        val g = goal(31)
        val now = at(today, 12)
        val fresh = GoalReminderEdits.newReminder(g.id, 1.0, true, false, now)
        report.check("$name: a new reminder is every day at 8:00 PM ('Every day 8:00 PM', 'Every day')", GoalReminderEdits.title(fresh, today) == "Every day 8:00 PM" && GoalReminderEdits.subtitle(fresh) == "Every day")
        val todayOne = GoalReminderEdits.withDayChoice(fresh, GoalReminderEdits.TODAY, today).copy(reason = "if not done yet")
        report.check("$name: the board's row: 'Today 8:00 PM', 'If not done yet · once'", GoalReminderEdits.title(todayOne, today) == "Today 8:00 PM" && GoalReminderEdits.subtitle(todayOne) == "If not done yet · once", GoalReminderEdits.subtitle(todayOne))
        report.check("$name: a stored Today reminder keeps its day on a later day (S1) and shows its date", GoalReminderEdits.dayPill(todayOne, today.plusDays(2)) == "Wed 11/11" && todayOne.date == today)
        val friday = GoalReminderEdits.withDayChoice(fresh, GoalReminderEdits.FIRST_WEEKDAY + 5, today)
        report.check("$name: Every Friday: 'Fri', every week, round trip of the choice", GoalReminderEdits.dayPill(friday, today) == "Fri" && GoalReminderEdits.repeatText(friday) == "Every week" && GoalReminderEdits.dayChoice(friday) == GoalReminderEdits.FIRST_WEEKDAY + 5 && friday.weekdays == listOf(GoalWeekday.Friday))
        val dated = GoalReminderEdits.withDayChoice(fresh, GoalReminderEdits.ON_A_DATE, today, LocalDate.of(2026, 12, 5))
        report.check("$name: On a date: 'Sat 12/5', once", GoalReminderEdits.dayPill(dated, today) == "Sat 12/5" && GoalReminderEdits.repeatText(dated) == "Once")
        val several = fresh.copy(days = GoalReminderDays.Weekdays, weekdays = listOf(GoalWeekday.Wednesday, GoalWeekday.Monday))
        report.check("$name: a stored set of weekdays keeps its own choice", GoalReminderEdits.dayChoice(several) == GoalReminderEdits.SEVERAL_WEEKDAYS && GoalReminderEdits.dayChoices(several).last() == "Mon, Wed" && GoalReminderEdits.dayPill(several, today) == "Mon, Wed")
        report.check("$name: times are 12-hour", GoalReminderEdits.timePill(LocalTime.of(20, 0)) == "8:00 PM" && GoalReminderEdits.timePill(LocalTime.of(0, 5)) == "12:05 AM")
        val numbered = GoalReminderEdits.withRules(listOf(friday, fresh.copy(reason = "  ")), skipWhenDone = false, nudgeWhenBehind = true)
        report.check("$name: the goal's rule switches go on every reminder, numbered in order, a blank reason dropped", numbered.map { it.position } == listOf(1.0, 2.0) && numbered.all { !it.skipWhenDone && it.nudgeWhenBehind } && numbered[1].reason == null)
        val stored = fresh.copy(editedAt = at(today.minusDays(1), 9))
        report.check("$name: 'Next: today 8:00 PM'", GoalReminderEdits.nextText(listOf(stored), g, now.toInstant(), zone) == "Next: today 8:00 PM", "${GoalReminderEdits.nextText(listOf(stored), g, now.toInstant(), zone)}")
        report.check("$name: 'Next: tomorrow 8:00 PM' after tonight's", GoalReminderEdits.nextText(listOf(stored), g, at(today, 21).toInstant(), zone) == "Next: tomorrow 8:00 PM")
        report.check("$name: a weekday reminder later this week: 'Next: Fri 8:00 PM'", GoalReminderEdits.nextText(listOf(friday.copy(editedAt = made)), g, now.toInstant(), zone) == "Next: Fri 8:00 PM")
        report.check("$name: a paused goal has no next reminder", GoalReminderEdits.nextText(listOf(stored), g.copy(pauses = listOf(GoalPauseStretch(today))), now.toInstant(), zone) == null)
    }

    private fun sleep(report: CheckReport, name: String) {
        val sleepRule = GoalSourceRule(id(950), GoalSourceType.HealthSleep)
        val hours = goal(41, GoalKind.Time, GoalTarget(GoalPeriod.Day, 420.0, GoalUnit.Minutes), sources = listOf(sleepRule))
        val other = goal(42, GoalKind.Time, GoalTarget(GoalPeriod.Day, 420.0, GoalUnit.Minutes))
        val off = goal(43, GoalKind.Time, GoalTarget(GoalPeriod.Day, 420.0, GoalUnit.Minutes), sources = listOf(sleepRule.copy(automatic = false)))
        val night = LocalDate.of(2026, 11, 10)
        val slept = listOf(
            GoalWindow(night.atTime(23, 0), today.atTime(5, 30)),
            GoalWindow(today.atTime(5, 0), today.atTime(7, 0)), // overlaps (two sources): counted once
            GoalWindow(today.atTime(14, 0), today.atTime(14, 20)), // a nap: the night of the 11th, read for the 12th
        )
        val byDay = GoalSleepReadings.minutesByDay(slept)
        report.check("$name: minutes asleep by the morning they end on, overlaps once, a nap the next day's", byDay == mapOf(today to 480.0, today.plusDays(1) to 20.0), "$byDay")
        val read = Instant.parse("2026-11-11T13:00:00Z")
        val entries = GoalSleepReadings.entries(listOf(hours, other, off, GoalDefaults.newDeepWork(areaA, made)), slept, read, zone)
        report.check("$name: readings only for goals with a switched-on sleep source (never Deep work), from Health Connect", entries.map { it.goalId }.toSet() == setOf(hours.id) && entries.all { it.source == GoalEntrySource.HealthConnect })
        report.check("$name: none before Health Connect was read (no permission, nothing read)", GoalSleepReadings.entries(listOf(hours), slept, null, zone).isEmpty())
        val data = GoalSourceData(emptyList(), entries, zone)
        val day = GoalCounting.day(hours, data, today, today)
        report.check("$name: a 7-hour sleep goal counts the night (480 min: hit)", day.value == 480.0 && day.hit, "$day")
    }

    private fun carry(report: CheckReport, name: String) {
        report.check("$name: Deep work weekly: saving another edit leaves a waiting lowering alone (field never touched)", !GoalDrafts.weeklyChangeAsked(600, 600, waiting = true, fieldEdited = false))
        report.check("$name: typing the current target back while a lowering waits drops it", GoalDrafts.weeklyChangeAsked(600, 600, waiting = true, fieldEdited = true))
        report.check("$name: a new amount is always asked", GoalDrafts.weeklyChangeAsked(300, 600, waiting = false, fieldEdited = true) && GoalDrafts.weeklyChangeAsked(null, 600, waiting = false, fieldEdited = false))
        report.check("$name: nothing to ask: same amount, nothing waiting", !GoalDrafts.weeklyChangeAsked(600, 600, waiting = false, fieldEdited = true))
    }

    // The rules and the phone's ringing code read no clock of their own: their "now" is the caller's trusted time (TrustedTime, ADR 70).
    private fun trusted(report: CheckReport, name: String) {
        val clockCalls = Regex("""(Instant|OffsetDateTime|LocalDate|LocalDateTime|ZonedDateTime|LocalTime)\.now\(|System\.currentTimeMillis\(""")
        val shared = File("../shared/src/main/java/com/accountabilibuddy/shared/goals")
        val pure = listOf("GoalReminderPlan.kt", "GoalReminderTimes.kt", "GoalReviewRules.kt", "GoalReminderEdits.kt", "GoalNudge.kt", "GoalSleepReadings.kt", "GoalLocalTime.kt")
        val reading = pure.filter { file -> File(shared, file).let { !it.exists() || clockCalls.containsMatchIn(it.readText()) } }
        report.check("$name: the reminder and review rules read no clock (now is passed in)", reading.isEmpty(), "reads a clock or missing: $reading")
        val app = File("../app/src/main/java/com/accountabilibuddy/companion")
        val ringing = listOf("notify/GoalReminders.kt", "goals/PhoneGoalReview.kt").map { File(app, it) }
        val decides = ringing.filter { file -> !file.exists() || !file.readText().contains("TrustedTime.now()") || Regex("""(Instant|OffsetDateTime|LocalDate|LocalDateTime)\.now\(""").containsMatchIn(file.readText()) }
        report.check("$name: the phone's ringing and review code takes its time from TrustedTime only", decides.isEmpty(), "not trusted-only: ${decides.map { it.name }}")
    }
}
