package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.shared.FocusPart
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Pre-Work on the phone (ADR 31): during a Pre-Work part with no session running anywhere, a quiet notice that stays
 * (also on the lock screen), "Pre-work until 8:30 · ease in, don't get pulled in", with the pickups since it began.
 */
object PreWorkNotice {
    private const val ID = 7

    /** The Pre-Work part now, when no session runs; null otherwise. */
    fun part(focus: WatchSnapshot?, now: Instant): FocusPart? =
        focus?.takeIf { it.running == null }?.parts?.firstOrNull { it.kind == "prework" && it.start <= now && now < it.end }

    /** @param pickups unlocks since the part began; null without Usage access. */
    fun check(context: Context, part: FocusPart?, pickups: Int?, clock: ClockFormat) {
        val manager = NotificationManagerCompat.from(context)
        if (part == null || !TimelineNotifier.canPost(context)) {
            manager.cancel(ID)
            return
        }

        val until = clock.time(LocalDateTime.ofInstant(part.end, ZoneId.systemDefault()))
        val text = when (pickups) {
            null, 0 -> "Ease in: plan, read, get ready. Don't get pulled into work or your feed yet."
            1 -> "Picked up once so far. Ease in: plan, read, get ready."
            else -> "Picked up $pickups times so far. Ease in: plan, read, get ready."
        }
        val open = PendingIntent.getActivity(
            context, 7, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.PARKED)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Pre-work until $until")
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle())
            .setOngoing(true)
            .setDeleteIntent(RefreshReceiver.dismissedIntent(context, 107))
            .setSilent(true)
            .setOnlyAlertOnce(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setContentIntent(open)
            .setColor(0xFFD97706.toInt())
            .build()
        try {
            manager.notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
