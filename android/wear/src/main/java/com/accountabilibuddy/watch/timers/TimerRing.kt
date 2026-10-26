package com.accountabilibuddy.watch.timers

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.watch.R
import com.accountabilibuddy.watch.WatchActivity

/**
 * A timer's "Time's up" notification: full screen when the watch is asleep, opening the Timers page, with Dismiss and
 * +1 min. Silent itself: the buzz is the phase-end one, which follows the Alerts page's choices.
 */
object TimerRing {
    private const val CHANNEL = "timers"
    private const val NOTIFICATION_BASE = 1_000
    const val ACTION_DISMISS = "com.accountabilibuddy.watch.TIMER_DISMISS"
    const val ACTION_PLUS_MINUTE = "com.accountabilibuddy.watch.TIMER_PLUS_MINUTE"

    fun show(context: Context, timer: WatchTimer) {
        val manager = context.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Timers", NotificationManager.IMPORTANCE_HIGH).apply {
                setSound(null, null)
                enableVibration(false)
            },
        )
        val open = PendingIntent.getActivity(
            context, NOTIFICATION_BASE + timer.id, WatchActivity.timersIntent(context),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        fun action(action: String, offset: Int) = PendingIntent.getBroadcast(
            context, NOTIFICATION_BASE * offset + timer.id,
            Intent(context, TimerActionReceiver::class.java).setAction(action).putExtra(TimerEndReceiver.ID, timer.id),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("Time's up")
            .setContentText("${timer.name} · ${TimerText.length(timer.length)}")
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setContentIntent(open)
            .setFullScreenIntent(open, true)
            .setAutoCancel(true)
            .addAction(0, "Dismiss", action(ACTION_DISMISS, 2))
            .addAction(0, "+1 min", action(ACTION_PLUS_MINUTE, 3))
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(NOTIFICATION_BASE + timer.id, notification) }
    }

    /** Takes down the notification of each timer that's no longer done (dismissed, or given another minute). */
    fun cancelGone(context: Context, timers: List<WatchTimer>) {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        val done = timers.filter { it.isDone(java.time.Instant.now()) }.map { NOTIFICATION_BASE + it.id }.toSet()
        manager.activeNotifications
            .filter { it.notification.channelId == CHANNEL && it.id !in done }
            .forEach { manager.cancel(it.id) }
    }
}
