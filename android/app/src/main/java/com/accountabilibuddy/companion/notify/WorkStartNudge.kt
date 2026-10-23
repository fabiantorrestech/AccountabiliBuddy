package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WorkStart
import java.time.Instant

/**
 * The start nudge on the phone: once per work stretch, in its first 30 minutes and only while no session runs on any of
 * the user's devices, one notification "Work started · plan 3 tasks and start". It goes away when a session starts.
 * The stretch already nudged is remembered (preferences "nudges").
 */
object WorkStartNudge {
    private const val ID = 5
    private const val PREFS = "nudges"
    private const val LAST = "workStart"

    fun check(context: Context, focus: WatchSnapshot?, now: Instant = Instant.now()) {
        if (focus == null) {
            // Nothing known, or the phone is paused: no nudge, and an old one goes.
            NotificationManagerCompat.from(context).cancel(ID)
            return
        }

        if (focus.running != null) {
            NotificationManagerCompat.from(context).cancel(ID)
            return
        }

        val stretch = WorkStart.due(focus, now) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(LAST, null) == stretch.toString() || !TimelineNotifier.canPost(context)) {
            return
        }

        prefs.edit().putString(LAST, stretch.toString()).apply()
        val open = PendingIntent.getActivity(
            context, 5, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.NUDGE)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Work started · plan 3 tasks and start")
            .setContentText("No session is running yet. Write down what you're doing first, start on your computer, and put the phone away.")
            .setStyle(NotificationCompat.BigTextStyle())
            .setAutoCancel(true)
            .setContentIntent(open)
            .setColor(0xFF2A9D8F.toInt())
            .setTimeoutAfter(WorkStart.NUDGE_WITHIN.toMillis())
            .build()
        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
