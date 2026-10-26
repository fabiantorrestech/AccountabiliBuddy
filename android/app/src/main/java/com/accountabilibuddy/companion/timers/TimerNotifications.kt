package com.accountabilibuddy.companion.timers

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.TimelineNotifier

/**
 * One notification per running or paused timer: its name, a live countdown (the chronometer counts down by itself)
 * and a bar (redrawn on each progress tick, every 20 s or so), with Pause or Resume, +1 min and Dismiss. A timer that's
 * up swaps it for the ringing one ([TimerRinger]), under the same id.
 */
object TimerNotifications {
    private const val TIMER_ID = 400_000
    private const val ALARM_ID = 500_000
    private const val ACTION_REQUEST = 600_000
    private const val BAR_MAX = 1000
    private const val COLOR = 0xFF0D9488.toInt()

    fun id(kind: RingingItem.Kind, id: Int): Int = (if (kind == RingingItem.Kind.TIMER) TIMER_ID else ALARM_ID) + id

    /** Posts or clears each timer's notification; one that's up is left to the ringer. */
    fun update(context: Context, timers: List<TimerItem>, nowMs: Long) {
        val manager = NotificationManagerCompat.from(context)
        timers.forEach { timer ->
            when (timer.state) {
                TimerState.RUNNING, TimerState.PAUSED ->
                    if (TimelineNotifier.canPost(context)) runCatching { manager.notify(id(RingingItem.Kind.TIMER, timer.id), build(context, timer, nowMs)) }
                TimerState.IDLE -> manager.cancel(id(RingingItem.Kind.TIMER, timer.id))
                // Ringing: the ringer's notification. Not ringing (e.g. after a restart): no countdown left behind.
                TimerState.DONE ->
                    if (TimerRinger.ringing.value.none { it.isSame(RingingItem.Kind.TIMER, timer.id) }) manager.cancel(id(RingingItem.Kind.TIMER, timer.id))
            }
        }
    }

    fun cancel(context: Context, kind: RingingItem.Kind, id: Int) = NotificationManagerCompat.from(context).cancel(id(kind, id))

    private fun build(context: Context, timer: TimerItem, nowMs: Long): android.app.Notification {
        val running = timer.state == TimerState.RUNNING
        val builder = NotificationCompat.Builder(context, Channels.TIMERS)
            .setSmallIcon(R.drawable.ic_stat_timer)
            .setContentTitle(timer.name)
            .setProgress(BAR_MAX, (timer.progress(nowMs) * BAR_MAX).toInt(), false)
            .setOngoing(true)
            .setSilent(true)
            .setOnlyAlertOnce(true)
            .setCategory(NotificationCompat.CATEGORY_STOPWATCH)
            .setContentIntent(openApp(context))
            // Android 14+ lets an ongoing notification be swiped: it's simply posted again.
            .setDeleteIntent(action(context, TimerReceiver.ACTION_REPOST, timer.id, 0))
            .setColor(COLOR)
            .setSortKey("timer-%06d".format(timer.id))
        if (running) {
            builder.setUsesChronometer(true)
                .setChronometerCountDown(true)
                .setWhen(timer.endAtMs ?: nowMs)
                .setShowWhen(true)
                .setContentText("${TimerItem.formatLength(timer.totalMs)} timer")
                .addAction(0, "Pause", action(context, TimerReceiver.ACTION_PAUSE, timer.id, 1))
        } else {
            builder.setShowWhen(false)
                .setContentText("Paused · ${TimerItem.formatLeft(timer.leftMs(nowMs))} left")
                .addAction(0, "Resume", action(context, TimerReceiver.ACTION_RESUME, timer.id, 2))
        }
        builder.addAction(0, "+1 min", action(context, TimerReceiver.ACTION_ADD_MINUTE, timer.id, 3))
            .addAction(0, "Dismiss", action(context, TimerReceiver.ACTION_DISMISS_TIMER, timer.id, 4))
        return builder.build()
    }

    /** A broadcast to [TimerReceiver] for item [id]; [slot] (0-7) keeps each button's request apart. */
    fun action(context: Context, action: String, id: Int, slot: Int): PendingIntent = PendingIntent.getBroadcast(
        context, ACTION_REQUEST + id * 8 + slot,
        Intent(context, TimerReceiver::class.java).setAction(action).putExtra(TimerReceiver.EXTRA_ID, id),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun openApp(context: Context): PendingIntent = PendingIntent.getActivity(
        context, 1, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
}
