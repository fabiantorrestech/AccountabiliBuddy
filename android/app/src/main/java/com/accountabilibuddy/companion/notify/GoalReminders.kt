package com.accountabilibuddy.companion.notify

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.PhoneDeepWork
import com.accountabilibuddy.companion.goals.PhoneGoalData
import com.accountabilibuddy.companion.goals.PhoneGoalReview
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.ui.shell.AppNav
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalNumbers
import com.accountabilibuddy.shared.goals.GoalPeriodStatus
import com.accountabilibuddy.shared.goals.GoalReminderMark
import com.accountabilibuddy.shared.goals.GoalReminderMarkKind
import com.accountabilibuddy.shared.goals.GoalReminderOccurrence
import com.accountabilibuddy.shared.goals.GoalReminderPlan
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSnooze
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalWeekStart
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/**
 * Goal reminders and the weekly review's notification on the phone (round 2 R6b3; brief §5.9, §5.10), through the phone's existing
 * notification path: the bedtime reminders' pattern (one exact alarm for the next one due, worked out again on every refresh, with the
 * USE_EXACT_ALARM and POST_NOTIFICATIONS the app already holds; nothing new is asked for). The rules are [GoalReminderPlan] and
 * [GoalReviewRules]; every time decision reads [TrustedTime] (ADR 70).
 * - **AccountabiliBuddy mode** rings like the phone's other reminders (a heads-up on the "Goal reminders" channel); **ProductaBuddy mode**
 *   rings once as a plain, silent notification. Each occurrence rings once; Snooze brings it back in 10 minutes.
 * - **Held during a focus session the user started** (and while the phone's guiding is paused): nothing rings; once the session ends the
 *   next refresh rings what is still due (the PC's "wait until the session ends").
 * - A paused goal sends nothing; "Skip when done" is checked when it comes due; the nudge rings once, at 9:00 AM the morning after.
 * - **Privacy (hard rule, R6a):** a goal's name and a reminder's reason are never put in a notification: it says "Goal reminder · 8:00 PM"
 *   and the kind with today's number ("daily habit · not yet · Behind"); a tap opens the goal. Nothing goes to the watch.
 * - Tick today (a habit not ticked yet) is a hand entry for today, as the goal menu's; Log opens the goal's page.
 */
object GoalReminders {
    const val ACTION_FIRE = "com.accountabilibuddy.companion.GOAL_REMINDER_FIRE"
    const val ACTION_TICK = "com.accountabilibuddy.companion.GOAL_REMINDER_TICK"
    const val ACTION_SNOOZE = "com.accountabilibuddy.companion.GOAL_REMINDER_SNOOZE"
    const val ACTION_REVIEW_SNOOZE = "com.accountabilibuddy.companion.GOAL_REVIEW_SNOOZE"
    const val EXTRA_KEY = "key"
    const val EXTRA_GOAL = "goal"
    const val EXTRA_ID = "id"

    /** How long Snooze on a goal reminder lasts. */
    val SNOOZE: Duration = Duration.ofMinutes(10)

    private const val REVIEW_ID = 7000
    private const val REMINDER_ID_BASE = 7100
    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /**
     * Rings what is due, marks what was skipped and sets the alarm for the next one. Called on every refresh ([Refresher]), when the
     * goals change, and when the alarm goes off. [paused]: the phone's guiding is paused (nothing rings).
     */
    fun refresh(context: Context, paused: Boolean) {
        val app = context.applicationContext as CompanionApp
        if (app.goals.status.value != PhoneGoalStatus.Ready) {
            cancelAlarm(context)
            return
        }

        val now = TrustedTime.now()
        val zone = ZoneId.systemDefault()
        val lists = app.goals.lists.value
        val pcDaily = PhoneDeepWork.pcDailyMinutes(app.sync.received.value.map { it.file })
        val weekly = app.goals.deepWork.current()
        val goals = lists.liveGoals.map { PhoneDeepWork.view(it, pcDaily, weekly) }
        val data = runCatching {
            PhoneGoalData.build(app.sync.allReceived.value.map { it.file }, goals, lists.liveEntries, app.steps.state.value, now, zone, PhoneGoalData.slept(app))
        }.getOrNull()
        val weekStart = GoalDisplayPrefs.display.value.weekStart
        val held = paused || PhoneGoalReview.sessionRunning(app, now)
        val marks = app.goalReminderMarks
        val step = GoalReminderPlan.step(lists.reminders, goals, marks.load(), data, weekStart, now, zone, held)
        marks.markAll(step.skip, GoalReminderMarkKind.Skipped, now)
        val productaBuddy = PhoneGoalReview.productaBuddy(app)
        if (data != null && TimelineNotifier.canPost(context)) {
            for (occurrence in step.ring) {
                val goal = goals.firstOrNull { it.id == occurrence.goalId } ?: continue
                if (post(context, goal, occurrence, data, weekStart, now, zone, productaBuddy)) {
                    marks.mark(occurrence.key, GoalReminderMark(GoalReminderMarkKind.Shown, now), now)
                }
            }
        }

        val reviewNext = reviewNotification(context, app, goals, held, now, zone)
        schedule(context, listOfNotNull(step.nextAt, reviewNext).minOrNull(), now)
    }

    /** Snooze on a notification: it rings again in [SNOOZE] (trusted time). */
    fun snooze(context: Context, key: String, notificationId: Int) {
        val app = context.applicationContext as CompanionApp
        val now = TrustedTime.now()
        val count = (app.goalReminderMarks.load().markOf(key)?.snoozeCount ?: 0) + 1
        app.goalReminderMarks.mark(key, GoalReminderMark(GoalReminderMarkKind.Snoozed, now, now.plus(SNOOZE), count), now)
        NotificationManagerCompat.from(context).cancel(notificationId)
        refresh(context, app.pause.isQuiet)
    }

    /** Tick today from the notification: a hand entry of 1 for today (as the goal menu's), then it is handled. */
    fun tick(context: Context, goalId: UUID, key: String, notificationId: Int) {
        val app = context.applicationContext as CompanionApp
        val now = TrustedTime.now()
        app.goals.addHandEntry(goalId, now.atZone(ZoneId.systemDefault()).toLocalDate(), 1.0)
        app.goalReminderMarks.mark(key, GoalReminderMark(GoalReminderMarkKind.Dismissed, now), now)
        NotificationManagerCompat.from(context).cancel(notificationId)
    }

    /** Snooze on the review's notification: as the card's default snooze (tomorrow; ProductaBuddy for the week). */
    fun snoozeReview(context: Context) {
        val app = context.applicationContext as CompanionApp
        PhoneGoalReview.snooze(app, GoalReviewSnooze.Tomorrow)
        NotificationManagerCompat.from(context).cancel(REVIEW_ID)
        refresh(context, app.pause.isQuiet)
    }

    // The review's notification on its day (and in AccountabiliBuddy mode again when a snooze ends): once per moment, only while the card
    // shows, never during a session; "Remind me once" rings at its snooze's end and then stays quiet. Returns the next moment ahead.
    private fun reviewNotification(context: Context, app: CompanionApp, goals: List<Goal>, held: Boolean, now: Instant, zone: ZoneId): Instant? {
        val moment = PhoneGoalReview.moment(app, goals, zone)
        if (moment.goals.isEmpty() || GoalReviewRules.isComplete(moment.record, moment.goals)) {
            NotificationManagerCompat.from(context).cancel(REVIEW_ID)
            return null
        }
        val ringAt = GoalReviewRules.ringAt(moment.due, moment.record, moment.productaBuddy) ?: return null
        val marks = app.goalReminderMarks.load()
        val quiet = marks.markOf(PhoneGoalReview.quietKey(moment.week))
        if (quiet != null && ringAt != quiet.snoozedUntil) return null
        if (ringAt > now) return ringAt
        val key = PhoneGoalReview.ringKey(moment.week, ringAt)
        if (held || !moment.row.shown || marks.markOf(key) != null || !TimelineNotifier.canPost(context)) return null
        val count = if (moment.goals.size == 1) "1 goal" else "${moment.goals.size} goals"
        val builder = NotificationCompat.Builder(context, Channels.GOALS)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Weekly goals review")
            .setContentText("$count · about ${GoalReviewRules.minutesFor(moment.goals.size)} minutes · week of ${GoalReviewRules.weekText(moment.week, compact = true)}")
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setAutoCancel(true)
            .setSilent(moment.productaBuddy)
            .setOnlyAlertOnce(true)
            .setContentIntent(open(context, "goals", REVIEW_ID))
            .setColor(0xFF2A9D8F.toInt())
        if (!moment.productaBuddy) {
            builder.addAction(0, "Snooze", broadcast(context, ACTION_REVIEW_SNOOZE, REVIEW_ID + 1) {})
        }
        runCatching { NotificationManagerCompat.from(context).notify(REVIEW_ID, builder.build()) }
        app.goalReminderMarks.mark(key, GoalReminderMark(GoalReminderMarkKind.Shown, now), now)
        return null
    }

    private fun post(
        context: Context,
        goal: Goal,
        occurrence: GoalReminderOccurrence,
        data: GoalSourceData,
        weekStart: GoalWeekStart,
        now: Instant,
        zone: ZoneId,
        productaBuddy: Boolean,
    ): Boolean {
        val today = now.atZone(zone).toLocalDate()
        val facts = GoalFactsBuilder.build(goal, data, today, weekStart)
        val id = REMINDER_ID_BASE + (occurrence.key.hashCode() and 0x3FF)
        val title = if (occurrence.nudge) {
            "Goal nudge · behind ${GoalTexts.periodName(goal.target.period)}"
        } else {
            "Goal reminder · ${occurrence.due.atZoneSameInstant(zone).format(CLOCK)}${if (occurrence.snoozeCount > 0) " · snoozed" else ""}"
        }
        // No goal name and no reason here (hard rule): the kind and today's number say enough; the goal opens on a tap.
        val text = "${GoalTexts.kindText(goal).replaceFirstChar { it.uppercase() }} · ${GoalNumbers.number(facts)} · ${statusText(facts.status)}"
        val public = NotificationCompat.Builder(context, Channels.GOALS)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Goal reminder")
            .build()
        val builder = NotificationCompat.Builder(context, Channels.GOALS)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(title)
            .setContentText(text)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setPublicVersion(public)
            .setAutoCancel(true)
            .setSilent(productaBuddy)
            .setContentIntent(open(context, "goals/${goal.id}", id))
            .setColor(0xFF2A9D8F.toInt())
        when {
            goal.isDeepWork -> Unit
            goal.kind == GoalKind.Habit && !facts.todayState.hit && !facts.isPaused ->
                builder.addAction(0, "Tick today", broadcast(context, ACTION_TICK, id + 2000) { putExtra(EXTRA_GOAL, goal.id.toString()).putExtra(EXTRA_KEY, occurrence.key).putExtra(EXTRA_ID, id) })
            goal.kind == GoalKind.Amount || goal.kind == GoalKind.Time ->
                builder.addAction(0, if (goal.kind == GoalKind.Time) "Add time" else "Log", open(context, "goals/${goal.id}", id + 1000))
        }
        builder.addAction(0, "Snooze 10 min", broadcast(context, ACTION_SNOOZE, id + 3000) { putExtra(EXTRA_KEY, occurrence.key).putExtra(EXTRA_ID, id) })
        return runCatching { NotificationManagerCompat.from(context).notify(id, builder.build()) }.isSuccess
    }

    private fun statusText(status: GoalPeriodStatus): String = when (status) {
        GoalPeriodStatus.OnTrack -> "On track"
        GoalPeriodStatus.Behind -> "Behind"
        GoalPeriodStatus.Paused -> "Paused"
        else -> "Not due yet"
    }

    // One exact alarm for the next goal reminder or review moment; the trusted time's distance to it, set on the alarm clock.
    private fun schedule(context: Context, next: Instant?, now: Instant) {
        val manager = context.getSystemService(AlarmManager::class.java)
        val pending = firePending(context)
        if (next == null) {
            manager.cancel(pending)
            return
        }
        val delay = Duration.between(now, next).toMillis().coerceAtLeast(1000L)
        val at = System.currentTimeMillis() + delay
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || manager.canScheduleExactAlarms()) {
            manager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
        } else {
            manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
        }
    }

    private fun cancelAlarm(context: Context) = context.getSystemService(AlarmManager::class.java).cancel(firePending(context))

    private fun firePending(context: Context): PendingIntent = PendingIntent.getBroadcast(
        context, 41, Intent(context, GoalReminderReceiver::class.java).setAction(ACTION_FIRE),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun broadcast(context: Context, action: String, requestCode: Int, extras: Intent.() -> Unit): PendingIntent = PendingIntent.getBroadcast(
        context, requestCode, Intent(context, GoalReminderReceiver::class.java).setAction(action).apply(extras),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun open(context: Context, route: String, requestCode: Int): PendingIntent = PendingIntent.getActivity(
        context, requestCode,
        Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP).putExtra(AppNav.EXTRA_OPEN, route),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
}
