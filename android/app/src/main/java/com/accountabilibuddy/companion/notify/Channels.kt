package com.accountabilibuddy.companion.notify

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context

/** Notification channels; both silent (no sound, no vibration). */
object Channels {
    // "timeline_top" since the channel went to high importance (a channel's importance can't change once made).
    const val TIMELINE = "timeline_top"
    private const val OLD_TIMELINE = "timeline"
    const val BEDTIME = "bedtime"
    const val BEDTIME_COUNTDOWN = "bedtime_countdown"
    const val BEDTIME_ALARM = "bedtime_alarm"
    const val NOISE = "noise"
    const val NUDGE = "nudge"

    /** The work-time nudge: pops down over the app in use (heads-up), with a buzz. */
    const val WORK_NUDGE = "work_nudge"
    const val PARKED = "parked"

    fun create(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java)
        // High importance (silenced, alerting once) ranks the timeline above other apps' default-importance pinned
        // cards, such as a calendar widget's; it never pops down because it never makes a sound or buzz.
        manager.createNotificationChannel(silent(TIMELINE, "Today's timeline", "Always-on timeline of your day", NotificationManager.IMPORTANCE_HIGH))
        // Refused while its card is still pinned by the service; tried again at the next start.
        runCatching { manager.deleteNotificationChannel(OLD_TIMELINE) }
        manager.createNotificationChannel(
            NotificationChannel(BEDTIME, "Bedtime reminders", NotificationManager.IMPORTANCE_HIGH).apply { description = "Your reminders before bedtime" },
        )
        manager.createNotificationChannel(silent(BEDTIME_COUNTDOWN, "Bedtime countdown", "From the first reminder until bedtime", NotificationManager.IMPORTANCE_LOW))
        // The alarm screen plays its own sound and vibration.
        manager.createNotificationChannel(silent(BEDTIME_ALARM, "Bedtime alarm", "A bedtime reminder that rings", NotificationManager.IMPORTANCE_HIGH))
        manager.createNotificationChannel(silent(NOISE, "Noise player", "While a sound plays", NotificationManager.IMPORTANCE_LOW))
        // The start nudge buzzes once (no sound): it's meant to be noticed.
        manager.createNotificationChannel(
            NotificationChannel(NUDGE, "Work start", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "When planned work starts and no session runs"
                setSound(null, null)
                enableVibration(true)
                setShowBadge(false)
            },
        )
        manager.createNotificationChannel(
            NotificationChannel(WORK_NUDGE, "Work-time nudge", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "During work time with no session: pops down over the app you're in"
                setSound(null, null)
                enableVibration(true)
                setShowBadge(false)
            },
        )
        manager.createNotificationChannel(silent(PARKED, "Phone parked", "While a focus session runs on your computer", NotificationManager.IMPORTANCE_LOW))
    }

    private fun silent(id: String, name: String, description: String, importance: Int) =
        NotificationChannel(id, name, importance).apply {
            this.description = description
            setSound(null, null)
            enableVibration(false)
            setShowBadge(false)
        }
}
