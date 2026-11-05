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
import java.time.Duration
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
    private const val POSTED_END = "postedStretchEnd"

    /** Nudge again every this many minutes (5, 10 or 15); 0 = once per stretch. 5 by default. */
    fun repeatMinutes(context: Context): Int = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getInt(REPEAT, 5)

    fun setRepeatMinutes(context: Context, minutes: Int) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putInt(REPEAT, minutes).apply()
    }

    /** When the next nudge could come, for the next refresh. */
    fun nextAt(context: Context, focus: WatchSnapshot?, now: Instant = Instant.now()): Instant? =
        WorkStart.nextNudgeAt(focus, now, lastNudge(context), com.accountabilibuddy.companion.nudge.GoalBonus.repeatMinutes(focus, repeatMinutes(context), now))

    private fun lastNudge(context: Context): Instant? =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(LAST, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }

    fun check(context: Context, focus: WatchSnapshot?, now: Instant = Instant.now()) {
        if (focus == null) {
            // Nothing known, or the phone is paused: no nudge, and an old one goes.
            NotificationManagerCompat.from(context).cancel(ID)
            return
        }

        // A session runs, or lunch (from any device): no nudge, as on the PC.
        val onLunch = (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.isOnLunch(now)
        if (focus.running != null || onLunch) {
            NotificationManagerCompat.from(context).cancel(ID)
            return
        }

        // No work stretch now (it ended, or none today): an old nudge goes.
        val stretchEnd = WorkStart.currentEnd(focus, now)
        if (stretchEnd == null) {
            NotificationManagerCompat.from(context).cancel(ID)
            return
        }

        val repeat = com.accountabilibuddy.companion.nudge.GoalBonus.repeatMinutes(focus, repeatMinutes(context), now)
        if (!TimelineNotifier.canPost(context)) return
        if (!WorkStart.nudgeDue(focus, now, lastNudge(context), repeat)) {
            extendShowing(context, stretchEnd, now)
            return
        }

        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(LAST, now.toString()).apply()
        val bonus = com.accountabilibuddy.companion.nudge.GoalBonus.of(focus, now)
        if (bonus != null) {
            post(context, "Goal met · bonus round?", "${com.accountabilibuddy.companion.nudge.GoalBonus.duration(bonus.focusedMinutes)} today. Time left in work hours: a bonus block counts extra.", stretchEnd, now, quietly = false)
        } else {
            post(context, "Work time · plan 3 tasks and start", "No session is running yet. Write down what you're doing first, start on your computer, and put the phone away.", stretchEnd, now, quietly = false)
        }
    }

    // The timeout is fixed when posted: while the notice still shows and the stretch now ends at another time (it got longer, say),
    // post it again silently with the same text and the new timeout, so it doesn't vanish while the stretch still holds.
    private fun extendShowing(context: Context, stretchEnd: Instant, now: Instant) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(POSTED_END, null) == stretchEnd.toString()) return
        val manager = context.getSystemService(android.app.NotificationManager::class.java) ?: return
        val showing = manager.activeNotifications.firstOrNull { it.id == ID }?.notification ?: return
        val title = showing.extras.getCharSequence(android.app.Notification.EXTRA_TITLE)?.toString() ?: return
        val text = showing.extras.getCharSequence(android.app.Notification.EXTRA_TEXT)?.toString() ?: return
        post(context, title, text, stretchEnd, now, quietly = true)
    }

    private fun post(context: Context, title: String, text: String, stretchEnd: Instant, now: Instant, quietly: Boolean) {
        val open = PendingIntent.getActivity(
            context, 5, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.WORK_NUDGE)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle())
            .setAutoCancel(true)
            .setOnlyAlertOnce(quietly)
            // Android removes it by itself when the stretch ends, even if the app is not running then.
            .setTimeoutAfter(Duration.between(now, stretchEnd).toMillis().coerceAtLeast(1L))
            .setContentIntent(open)
            .setColor(0xFF2A9D8F.toInt())
            .build()
        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
            context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(POSTED_END, stretchEnd.toString()).apply()
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
