package com.accountabilibuddy.shared.goals

import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/**
 * The weekly goals review's rules on the phone (round 2 R6b3), the PC's `GoalReviewService` for its pure parts: it comes at Settings ›
 * Goals' day and time (the week's first day at 6:00 PM by default) and covers the week before that day (on the user's week start); it
 * asks about every live goal not paused today (Deep work too), in list order, and stays until each has an answer. Snooze: AccountabiliBuddy
 * mode asks again once the snooze ends; ProductaBuddy mode keeps it snoozed for the week (the card stays, quietly). Never during a focus
 * session. Suggestions only: nothing here locks, waits or feeds a guard. Every "now" is the caller's trusted time.
 */
object GoalReviewRules {
    /** A snooze ends at this time of the chosen day (the board's "Mon, Nov 16 · 9:00 AM"). */
    val SNOOZE_TIME: LocalTime = LocalTime.of(9, 0)

    private val MONTH: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM", Locale.US)
    private val DAY_LINE: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE, MMM d", Locale.US)
    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /** The review's day: the one chosen, else the week's first day (the PC's `GoalPageSettings.ReviewDay`). */
    fun reviewDay(settings: GoalReviewSettings, weekStart: GoalWeekStart): DayOfWeek = settings.day ?: when (weekStart) {
        GoalWeekStart.Sunday -> DayOfWeek.SUNDAY
        GoalWeekStart.Monday -> DayOfWeek.MONDAY
        GoalWeekStart.Saturday -> DayOfWeek.SATURDAY
    }

    /** The latest review moment at or before [now] (the review day at its time, local). */
    fun dueAt(now: Instant, day: DayOfWeek, time: LocalTime, zone: ZoneId): OffsetDateTime {
        val today = now.atZone(zone).toLocalDate()
        for (back in 0..7) {
            val candidate = today.minusDays(back.toLong())
            if (candidate.dayOfWeek != day) continue
            val at = GoalLocalTime.at(candidate, time, zone)
            if (at.toInstant() <= now) return at
        }
        return GoalLocalTime.at(today.minusDays(7), time, zone)
    }

    /** The first day of the week a review due at [due] covers: the week (user's week start) of the day before. */
    fun weekOf(due: OffsetDateTime, weekStart: GoalWeekStart, zone: ZoneId): LocalDate =
        GoalPeriods.weekStartOf(due.atZoneSameInstant(zone).toLocalDate().minusDays(1), weekStart)

    /** The PC's "Nov 8 – 14" / "Oct 29 – Nov 4"; [compact] is the phone board's "Nov 9–15". */
    fun weekText(start: LocalDate, compact: Boolean = false): String {
        val end = start.plusDays(6)
        val dash = if (compact) "–" else " – "
        val from = "${start.format(MONTH)} ${start.dayOfMonth}"
        return if (start.month == end.month) "$from$dash${end.dayOfMonth}" else "$from$dash${end.format(MONTH)} ${end.dayOfMonth}"
    }

    /** The week's review record (answers and snooze); null when none was made yet. */
    fun recordFor(reviews: List<GoalReviewRecord>, week: LocalDate): GoalReviewRecord? =
        reviews.filter { it.isLive && it.week == week }.maxByOrNull { it.editedAt.toInstant() }

    /** The goals a review asks about: every live goal not paused today, areas in their order, then goals in theirs. */
    fun goalsToReview(goals: List<Goal>, areas: List<GoalArea>, today: LocalDate): List<Goal> {
        val areaPlace = areas.filter { it.isLive }.associate { it.id to it.position }
        return goals.filter { it.isLive && !it.isPausedOn(today) }
            .sortedWith(compareBy<Goal>({ areaPlace[it.areaId] ?: Double.MAX_VALUE }, { it.position }))
    }

    fun isComplete(record: GoalReviewRecord?, goals: List<Goal>): Boolean =
        goals.all { goal -> record?.decisions?.any { it.goalId == goal.id } == true }

    /** The next goal without an answer from [from] on (wrapping); null when every goal has one. */
    fun firstUnanswered(record: GoalReviewRecord?, goals: List<Goal>, from: Int): Int? {
        for (step in goals.indices) {
            val at = (from + step) % goals.size
            if (record?.decisions?.any { it.goalId == goals[at].id } != true) return at
        }
        return null
    }

    /** The answer kept for [goalId], if any. */
    fun answerOf(record: GoalReviewRecord?, goalId: UUID): GoalReviewChoice? = record?.decisions?.firstOrNull { it.goalId == goalId }?.choice

    /** Why [choice] can't be the answer for [goal] (Pause on Deep work, the PC's rule); null when it can. */
    fun refusal(goal: Goal, choice: GoalReviewChoice): String? =
        if (choice == GoalReviewChoice.Pause && !GoalLowering.canPauseOrDelete(goal)) "Deep work can't be paused" else null

    /** A decision row's title (the brief's four, §4.3). */
    fun choiceTitle(choice: GoalReviewChoice): String = when (choice) {
        GoalReviewChoice.KeepGoing -> "Keep going"
        GoalReviewChoice.Adjust -> "Adjust the target"
        GoalReviewChoice.Pause -> "Pause this goal"
        GoalReviewChoice.DecideNextWeek -> "Decide next week"
    }

    /** A decision row's muted line, as the PC's dialog words it ([waitMinutes]: the looser-change wait, for Deep work outside ProductaBuddy). */
    fun choiceSub(choice: GoalReviewChoice, goal: Goal, productaBuddy: Boolean, waitMinutes: Long): String = when (choice) {
        GoalReviewChoice.KeepGoing -> "Same target · no change"
        GoalReviewChoice.Adjust -> when {
            goal.isDeepWork -> "Its daily target is set on your PC · the weekly one here${if (productaBuddy) "" else " (lowering waits $waitMinutes min)"}"
            else -> "Move the date or the amount"
        }
        GoalReviewChoice.Pause -> refusal(goal, choice) ?: "Stops counting and reminders · keeps history"
        GoalReviewChoice.DecideNextWeek -> "Skips this goal in the review"
    }

    /** [record] with [choice] for [goalId], replacing an earlier answer for that goal. */
    fun withAnswer(record: GoalReviewRecord, goalId: UUID, choice: GoalReviewChoice): GoalReviewRecord =
        record.copy(decisions = record.decisions.filter { it.goalId != goalId } + GoalReviewDecision(goalId, choice))

    /**
     * When a snooze made at [now] ends: ProductaBuddy keeps it snoozed until next week's review ([due] + 7 days); AccountabiliBuddy until
     * 9:00 AM tomorrow or in 2 days ("Remind me once" is tomorrow).
     */
    fun snoozeUntil(choice: GoalReviewSnooze, productaBuddy: Boolean, due: OffsetDateTime, now: Instant, zone: ZoneId): OffsetDateTime {
        if (productaBuddy) return due.plusDays(7)
        val days = if (choice == GoalReviewSnooze.TwoDays) 2L else 1L
        return GoalLocalTime.at(now.atZone(zone).toLocalDate().plusDays(days), SNOOZE_TIME, zone)
    }

    /** "Mon, Nov 16 · 9:00 AM" for a snooze's end (12-hour). */
    fun snoozeLine(until: OffsetDateTime, zone: ZoneId): String {
        val local = until.atZoneSameInstant(zone)
        return "${local.format(DAY_LINE)} · ${local.format(CLOCK)}"
    }

    /** "Sundays", "Mondays". */
    fun dayPlural(day: DayOfWeek): String = day.name.lowercase().replaceFirstChar { it.uppercase() } + "s"

    /** About half a minute a goal, at least 1 ("About 4 minutes" for 8 goals). */
    fun minutesFor(goalCount: Int): Int = maxOf(1, (goalCount + 1) / 2)

    /**
     * The entry card now. [ready]: the goals can be used; [sessionRunning]: a focus session the user started runs (never during one);
     * [goals]: what the review asks about; [record]: this week's; [day]: the review day (for "Sundays").
     */
    fun row(
        ready: Boolean,
        sessionRunning: Boolean,
        goals: List<Goal>,
        record: GoalReviewRecord?,
        now: Instant,
        productaBuddy: Boolean,
        day: DayOfWeek,
    ): GoalReviewRowState {
        if (!ready || sessionRunning || goals.isEmpty() || isComplete(record, goals)) return GoalReviewRowState.HIDDEN
        val count = if (goals.size == 1) "1 goal" else "${goals.size} goals"
        val title = "Weekly goals review · $count"
        val note = "About ${minutesFor(goals.size)} minutes · ${dayPlural(day)}"
        val until = record?.snoozedUntil
        if (until != null && until.toInstant() > now) {
            return if (productaBuddy) {
                GoalReviewRowState(shown = true, snoozed = true, canSnooze = false, title = title, note = "Snoozed · start it whenever you like")
            } else {
                GoalReviewRowState(shown = false, snoozed = true, canSnooze = false, title = title, note = note)
            }
        }
        return GoalReviewRowState(shown = true, snoozed = false, canSnooze = true, title = title, note = note)
    }

    /**
     * When the review's notification rings for this week: at the due moment, and in AccountabiliBuddy mode again when a snooze ends;
     * null when nothing more rings this week (a ProductaBuddy snooze stays quiet). The caller rings each moment once, only while the
     * card shows, never during a session, and not after "Remind me once" has rung.
     */
    fun ringAt(due: OffsetDateTime, record: GoalReviewRecord?, productaBuddy: Boolean): Instant? {
        val until = record?.snoozedUntil?.toInstant()
        return when {
            until == null || until <= due.toInstant() -> due.toInstant()
            productaBuddy -> null
            else -> until
        }
    }
}
