package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.shared.RunningSession
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * "Phone parked": while a focus session runs on another of the user's devices, a quiet notification that stays (also
 * on the lock screen) and says until when, and how often the phone was picked up so far. Picking the phone up still
 * works; it's counted. Gone on a break and when the session ends.
 */
object ParkedNotice {
    private const val ID = 6

    /** @param pickups unlocks since the session started; null without Usage access. */
    fun check(context: Context, running: RunningSession?, pickups: Int?, clock: ClockFormat) {
        val manager = NotificationManagerCompat.from(context)
        if (running == null || running.onBreak || !TimelineNotifier.canPost(context)) {
            manager.cancel(ID)
            return
        }

        val until = running.endsAt?.let { clock.time(LocalDateTime.ofInstant(it, ZoneId.systemDefault())) }
        val title = if (until != null) "Phone parked until $until" else "Phone parked · session running"
        val text = when (pickups) {
            null -> "Put it away, out of sight, until the session's done."
            0 -> "Not picked up once so far. Keep it face down and out of reach."
            1 -> "Picked up once so far. Put it back out of reach."
            else -> "Picked up $pickups times so far. Put it back out of reach."
        }
        val open = PendingIntent.getActivity(
            context, 6, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.PARKED)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(title)
            .setContentText(text)
            .setOngoing(true)
            .setDeleteIntent(RefreshReceiver.dismissedIntent(context, 106))
            .setSilent(true)
            .setOnlyAlertOnce(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setContentIntent(open)
            .addAction(0, "Need my phone", PendingIntent.getActivity(
                context, 7, Intent(context, com.accountabilibuddy.companion.nudge.EmergencyActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            ))
            .setColor(0xFF2A9D8F.toInt())
            .build()
        try {
            manager.notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
