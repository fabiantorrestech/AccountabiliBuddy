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
        // Phase 11b: "Need it for work" under way, or just over ("Done? Put it back").
        val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
        val now = java.time.Instant.now()
        val workUse = app.workUse.active(now)
        val sessionStart = com.accountabilibuddy.companion.placement.PlacementSession.current(app, now)?.start
        // "Put it back" stays until the phone is back in a bag, face down or in a pocket (or 10 minutes pass).
        val back = app.placementStore.load()?.takeIf { it.active }?.state in setOf(
            com.accountabilibuddy.companion.placement.PlacementState.Bag,
            com.accountabilibuddy.companion.placement.PlacementState.Desk,
            com.accountabilibuddy.companion.placement.PlacementState.Pocket,
        )
        val workUseOver = workUse == null && !back && sessionStart != null && app.workUse.justEnded(sessionStart, now) != null
        val text = when {
            workUse != null -> "Work use until ${clock.time(LocalDateTime.ofInstant(workUse.end, ZoneId.systemDefault()))}. Put it back when you're done."
            workUseOver -> "Done? Put it back."
            else -> pickupsText(pickups)
        }
        val open = PendingIntent.getActivity(
            context, 6, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val builder = NotificationCompat.Builder(context, Channels.PARKED)
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
        if (workUse == null) {
            builder.addAction(0, "Need it for work", PendingIntent.getActivity(
                context, 8, Intent(context, com.accountabilibuddy.companion.placement.WorkUseActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            ))
        }
        val notification = builder.setColor(0xFF2A9D8F.toInt()).build()
        try {
            manager.notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }

    private fun pickupsText(pickups: Int?): String =
        when (pickups) {
            null -> "Put it away, out of sight, until the session's done."
            0 -> "Not picked up once so far. Keep it face down and out of reach."
            1 -> "Picked up once so far. Put it back out of reach."
            else -> "Picked up $pickups times so far. Put it back out of reach."
        }
}
