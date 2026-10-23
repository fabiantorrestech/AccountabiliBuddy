package com.accountabilibuddy.companion.pause

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.TimelineNotifier
import java.time.Instant
import java.time.ZoneId

/** Each morning while paused "until I turn it back on, remind me": one notification to turn the phone's guiding back on. */
object PauseReminder {
    private const val ID = 8

    fun check(context: Context, store: PauseStore, now: Instant = Instant.now()) {
        val zone = ZoneId.systemDefault()
        if (!PauseRules.reminderDue(store.state.value, now, zone) || !TimelineNotifier.canPost(context)) {
            return
        }

        store.reminded(now.atZone(zone).toLocalDate())
        val open = PendingIntent.getActivity(
            context, 8, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.NUDGE)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("AccountabiliBuddy is still off on this phone")
            .setContentText("Turn it back on in Settings when you're ready. Need set times off? Plan off days in the day plan instead.")
            .setStyle(NotificationCompat.BigTextStyle())
            .setAutoCancel(true)
            .setContentIntent(open)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }
}
