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
 * The start nudge on the phone: in a work stretch while no session runs on any of the user's devices, a notification
 * "Work time · plan 3 tasks and start", again every 5 minutes by default (10, 15, or once; preferences "nudges"). It
 * goes away when a session starts.
 */
object WorkStartNudge {
    private const val ID = 5
    private const val PREFS = "nudges"
    private const val LAST = "lastNudge"
    private const val REPEAT = "repeatMinutes"

    /** Nudge again every this many minutes (5, 10 or 15); 0 = once per stretch. 5 by default. */
    fun repeatMinutes(context: Context): Int = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getInt(REPEAT, 5)

    fun setRepeatMinutes(context: Context, minutes: Int) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putInt(REPEAT, minutes).apply()
    }

    /** When the next nudge could come, for the next refresh. */
    fun nextAt(context: Context, focus: WatchSnapshot?, now: Instant = Instant.now()): Instant? =
        WorkStart.nextNudgeAt(focus, now, lastNudge(context), repeatMinutes(context))

    private fun lastNudge(context: Context): Instant? =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(LAST, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }

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

        if (!WorkStart.nudgeDue(focus, now, lastNudge(context), repeatMinutes(context)) || !TimelineNotifier.canPost(context)) {
            return
        }

        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(LAST, now.toString()).apply()
        val open = PendingIntent.getActivity(
            context, 5, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.WORK_NUDGE)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Work time · plan 3 tasks and start")
            .setContentText("No session is running yet. Write down what you're doing first, start on your computer, and put the phone away.")
            .setStyle(NotificationCompat.BigTextStyle())
            .setAutoCancel(true)
            .setContentIntent(open)
            .setColor(0xFF2A9D8F.toInt())
            .build()
        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
