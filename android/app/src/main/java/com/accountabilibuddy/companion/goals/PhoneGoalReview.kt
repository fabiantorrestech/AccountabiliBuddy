package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.GuidanceMode
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalReviewChoice
import com.accountabilibuddy.shared.goals.GoalReviewRecord
import com.accountabilibuddy.shared.goals.GoalReviewRowState
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSnooze
import com.accountabilibuddy.shared.goals.GoalReminderMark
import com.accountabilibuddy.shared.goals.GoalReminderMarkKind
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * The weekly goals review on this phone (round 2 R6b3; brief §4.3, §5.8, §5.10; the PC's `GoalReviewService`): when it is due, what it
 * asks, and the answers, through the shared rules ([GoalReviewRules]) and the goals store (the week's [GoalReviewRecord], kept at once,
 * so Stop for now keeps them). Every time decision reads [TrustedTime] (ADR 70), never the phone's own clock; the session check is the
 * one the guard lock uses ([GuardsLock.at]). Never during a focus session the user started: the card hides, answers are refused, an open
 * review closes. Pause this goal pauses at once (refused for Deep work); Adjust the target only pre-fills Edit goal › Target. Suggestions
 * only: nothing here locks, waits or feeds a guard.
 */
object PhoneGoalReview {
    /** One reading of the review's state at a trusted moment. */
    data class Moment(
        val now: Instant,
        val due: OffsetDateTime,
        val week: LocalDate,
        val record: GoalReviewRecord?,
        val goals: List<Goal>,
        val row: GoalReviewRowState,
        val sessionRunning: Boolean,
        val productaBuddy: Boolean,
    )

    fun productaBuddy(app: CompanionApp): Boolean = app.guidance.state.value.mode == GuidanceMode.Companion

    fun sessionRunning(app: CompanionApp, now: Instant): Boolean = GuardsLock.at(app, now) != null

    /** The review now; [goals] are the live goals as the screens show them (Deep work as its view), or the stored ones. */
    fun moment(app: CompanionApp, goals: List<Goal>? = null, zone: ZoneId = ZoneId.systemDefault()): Moment {
        val now = TrustedTime.now()
        val settings = GoalSettingsPrefs.review.value
        val weekStart = GoalDisplayPrefs.display.value.weekStart
        val day = GoalReviewRules.reviewDay(settings, weekStart)
        val due = GoalReviewRules.dueAt(now, day, settings.time, zone)
        val week = GoalReviewRules.weekOf(due, weekStart, zone)
        val lists = app.goals.lists.value
        val today = now.atZone(zone).toLocalDate()
        val asked = GoalReviewRules.goalsToReview(goals ?: lists.liveGoals, lists.liveAreas, today)
        val record = GoalReviewRules.recordFor(lists.reviews, week)
        val session = sessionRunning(app, now)
        val pb = productaBuddy(app)
        val ready = app.goals.status.value == PhoneGoalStatus.Ready
        return Moment(now, due, week, record, asked, GoalReviewRules.row(ready, session, asked, record, now, pb, day), session, pb)
    }

    /** Snoozes this week's review ([choice]; ProductaBuddy: for the week). Answers made so far stay. Returns why it didn't, or null. */
    fun snooze(app: CompanionApp, choice: GoalReviewSnooze, zone: ZoneId = ZoneId.systemDefault()): String? {
        val moment = moment(app, zone = zone)
        if (moment.sessionRunning) return NOT_IN_SESSION
        val until = GoalReviewRules.snoozeUntil(choice, moment.productaBuddy, moment.due, moment.now, zone)
        val record = (moment.record ?: newRecord(app, moment.week)).copy(snoozedUntil = until)
        if (choice == GoalReviewSnooze.RemindOnce) {
            // "Remind me once · then quiet": the notification rings at the snooze's end once; after it, none this week.
            app.goalReminderMarks.mark(quietKey(moment.week), GoalReminderMark(GoalReminderMarkKind.Shown, moment.now, until.toInstant()), moment.now)
        }
        return app.goals.saveReview(record)
    }

    /**
     * One answer for [goalId], kept at once in the week's record (a later answer replaces it). Pause pauses the goal from today first and
     * is refused for Deep work (nothing is recorded then). Refused during a focus session.
     */
    fun decide(app: CompanionApp, week: LocalDate, goal: Goal, choice: GoalReviewChoice, zone: ZoneId = ZoneId.systemDefault()): String? {
        val now = TrustedTime.now()
        if (sessionRunning(app, now)) return NOT_IN_SESSION
        GoalReviewRules.refusal(goal, choice)?.let { return "$it." }
        if (choice == GoalReviewChoice.Pause) {
            app.goals.pauseGoal(goal.id, now.atZone(zone).toLocalDate())?.let { return it }
        }
        val record = GoalReviewRules.recordFor(app.goals.lists.value.reviews, week) ?: newRecord(app, week)
        return app.goals.saveReview(GoalReviewRules.withAnswer(record, goal.id, choice))
    }

    /** The mark key that says "Remind me once" was chosen for [week] (the notification stays quiet after its one ring). */
    fun quietKey(week: LocalDate): String = "review-quiet:$week"

    /** The mark key of the review's notification for [week] at [at] (each moment rings once). */
    fun ringKey(week: LocalDate, at: Instant): String = "review:$week:${at.epochSecond}"

    private fun newRecord(app: CompanionApp, week: LocalDate): GoalReviewRecord {
        val position = (app.goals.lists.value.reviews.maxOfOrNull { it.position } ?: 0.0) + 1.0
        return GoalReviewRecord(UUID.randomUUID(), week, emptyList(), position, OffsetDateTime.ofInstant(TrustedTime.now(), ZoneId.systemDefault()))
    }

    const val NOT_IN_SESSION = "Not during a focus session: the review waits until it ends."
}
