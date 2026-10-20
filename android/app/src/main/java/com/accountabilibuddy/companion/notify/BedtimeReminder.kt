package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.data.CompanionStore
import java.time.Duration
import com.accountabilibuddy.companion.engine.ClockFormat
import java.time.LocalDateTime

/**
 * Once a night, two hours before bedtime (or as soon as the phone checks within those two hours), a silent
 * reminder that stays until dismissed, like the Windows app's. The night already reminded about is remembered.
 */
object BedtimeReminder {
    private const val ID = 2
    private val LEAD: Duration = Duration.ofHours(2)

    fun check(context: Context, store: CompanionStore, clock: ClockFormat, now: LocalDateTime = LocalDateTime.now()) {
        val state = store.state.value
        if (!state.sleep.enabled || !state.bedtimeReminderOn || !TimelineNotifier.canPost(context)) {
            return
        }

        val night = state.sleep.toSchedule(state.document.dayPlan).nightAround(now)
        val untilBed = Duration.between(now, night.start)
        val key = night.start.toString()
        if (!night.end.isAfter(night.start) || untilBed.isNegative || untilBed.isZero || untilBed > LEAD || state.lastReminderNight == key) {
            return
        }

        store.setLastReminderNight(key)
        val sleep = Duration.between(night.start, night.end)
        val open = PendingIntent.getActivity(
            context, 1, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, Channels.BEDTIME)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Bedtime in ${hoursMinutes(untilBed)}")
            .setContentText("Bed at ${clock.time(night.start)} gives you ${hoursMinutes(sleep)} of sleep (up at ${clock.time(night.end)}). Time to start winding down.")
            .setStyle(NotificationCompat.BigTextStyle())
            .setSilent(true)
            .setAutoCancel(true)
            .setContentIntent(open)
            .setColor(0xFF6366F1.toInt())
            .setGroup(Channels.BEDTIME)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile.
        }
    }

    /** "1 h 55 min", rounded up to the minute, like the Windows reminder. */
    private fun hoursMinutes(span: Duration): String {
        val minutes = (span.seconds + 59) / 60
        val hours = minutes / 60
        val rest = minutes % 60
        return when {
            hours == 0L -> "$rest min"
            rest == 0L -> "$hours h"
            else -> "$hours h $rest min"
        }
    }
}
