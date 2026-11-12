package com.accountabilibuddy.shared.goals

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

/**
 * Goal reminders on the phone (round 2 R6b3; brief §5.9, §5.10), the PC's `GoalReminderSource` and its engine's due rule, as pure rules:
 * - each live reminder of a live goal rings at its local time on its days ([GoalReminderTimes]); a goal paused today sends nothing; only
 *   today's occurrences ring (yesterday's only when snoozed into today), and one missed by more than [LOOKBACK] doesn't;
 * - "Skip when the goal is already done" is checked when it comes due ([isDone]): done then, it is marked skipped and never rings;
 * - "Nudge when the week falls behind" (on any of the goal's reminders) rings once at 9:00 AM the morning after the goal drops below
 *   pace ([GoalNudge]);
 * - nothing comes due before the goal data is in ([data] null), so "Skip when done" never reads an empty history;
 * - **the session rule:** during a focus session the user started, goal reminders are held, never rung (the PC's "During a focus
 *   session" rule with its default, wait until it ends; goal reminders are never Can't miss), and come up when it ends.
 * Suggestions only: nothing locks or waits; never exported, synced or sent to the watch. [now] is the caller's trusted time.
 */
object GoalReminderPlan {
    /** How long after its time an occurrence still rings (the phone was off), as task reminders. */
    val LOOKBACK: Duration = Duration.ofHours(24)

    fun reminderKey(reminderId: UUID, due: Instant): String = "r:$reminderId:${due.epochSecond}"

    fun nudgeKey(goalId: UUID, day: LocalDate): String = "nudge:$goalId:$day"

    /** Whether a goal counts as done today for "Skip when done": today hit, or the period's target met (the PC's `IsDone`). */
    fun isDone(facts: GoalFacts): Boolean {
        if (!facts.hasPeriodTarget) return false
        return facts.todayState.hit || (facts.periodGoal.target.period != GoalPeriod.Day && facts.period.met)
    }

    /**
     * @param goals The live goals as the screens show them (Deep work as its view).
     * @param data The goal data, or null before it is first read.
     * @param held True while a focus session the user started runs, or the phone's guiding is paused: nothing rings.
     */
    fun step(
        reminders: List<GoalReminder>,
        goals: List<Goal>,
        state: GoalReminderState,
        data: GoalSourceData?,
        weekStart: GoalWeekStart,
        now: Instant,
        zone: ZoneId,
        held: Boolean,
    ): GoalReminderStep {
        val today = now.atZone(zone).toLocalDate()
        val dayStart = GoalLocalTime.at(today, java.time.LocalTime.MIDNIGHT, zone).toInstant()
        val byId = goals.filter { it.isLive }.associateBy { it.id }
        val facts = mutableMapOf<UUID, GoalFacts>()
        fun factsOf(goal: Goal): GoalFacts = facts.getOrPut(goal.id) { GoalFactsBuilder.build(goal, data!!, today, weekStart) }

        val occurrences = mutableListOf<GoalReminderOccurrence>()
        val skip = mutableListOf<String>()
        for (reminder in reminders.filter { it.isLive }) {
            val goal = byId[reminder.goalId] ?: continue
            if (goal.isPausedOn(today)) continue
            for (day in listOf(today.minusDays(1), today)) {
                val due = GoalReminderTimes.on(reminder, day, zone) ?: continue
                val key = reminderKey(reminder.id, due.toInstant())
                val mark = state.markOf(key)
                if (mark != null && mark.isHandled) continue
                val ringAt = mark?.snoozedUntil ?: due.toInstant()
                if (day < today && ringAt < dayStart) continue
                if (ringAt <= now) {
                    if (data == null) continue
                    if (reminder.skipWhenDone && isDone(factsOf(goal))) {
                        skip += key
                        continue
                    }
                }
                occurrences += GoalReminderOccurrence(goal.id, reminder.id, false, due, ringAt, key, mark?.snoozeCount ?: 0)
            }
        }

        if (data != null) {
            val nudging = reminders.filter { it.isLive && it.nudgeWhenBehind }.map { it.goalId }.toSet()
            for (goalId in nudging) {
                val goal = byId[goalId] ?: continue
                if (goal.isPausedOn(today)) continue
                val yesterdayMark = state.markOf(nudgeKey(goalId, today.minusDays(1)))
                val until = yesterdayMark?.snoozedUntil
                if (yesterdayMark?.kind == GoalReminderMarkKind.Snoozed && until != null && until >= dayStart &&
                    GoalStatus.thisPeriod(goal, data, today, weekStart) == GoalPeriodStatus.Behind
                ) {
                    val due = GoalReminderTimes.nudgeOn(today.minusDays(1), zone)
                    occurrences += GoalReminderOccurrence(goalId, goalId, true, due, until, nudgeKey(goalId, today.minusDays(1)), yesterdayMark.snoozeCount)
                }

                val key = nudgeKey(goalId, today)
                val mark = state.markOf(key)
                if ((mark != null && mark.isHandled) || !GoalNudge.droppedBehind(goal, data, today, weekStart)) continue
                val due = GoalReminderTimes.nudgeOn(today, zone)
                occurrences += GoalReminderOccurrence(goalId, goalId, true, due, mark?.snoozedUntil ?: due.toInstant(), key, mark?.snoozeCount ?: 0)
            }
        }

        val dueNow = occurrences
            .filter { it.ringAt <= now && Duration.between(it.ringAt, now) <= LOOKBACK }
            .sortedWith(compareBy<GoalReminderOccurrence>({ it.ringAt }, { it.goalId.toString() }, { it.reminderId.toString() }))
        val nextAt = occurrences.map { it.ringAt }.filter { it > now }.minOrNull()
        return if (held) {
            GoalReminderStep(emptyList(), skip, dueNow, nextAt)
        } else {
            GoalReminderStep(dueNow, skip, emptyList(), nextAt)
        }
    }
}
