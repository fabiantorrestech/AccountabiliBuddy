package com.accountabilibuddy.companion.notify

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context

/** Notification channels; both silent (no sound, no vibration). */
object Channels {
    const val TIMELINE = "timeline"
    const val BEDTIME = "bedtime"
    const val NOISE = "noise"

    fun create(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java)
        // Default importance (silenced) keeps the timeline near the top, unlike "low", which sinks it.
        manager.createNotificationChannel(silent(TIMELINE, "Today's timeline", "Always-on timeline of your day", NotificationManager.IMPORTANCE_DEFAULT))
        manager.createNotificationChannel(silent(BEDTIME, "Bedtime reminder", "Two hours before bedtime", NotificationManager.IMPORTANCE_DEFAULT))
        manager.createNotificationChannel(silent(NOISE, "Noise player", "While a sound plays", NotificationManager.IMPORTANCE_LOW))
    }

    private fun silent(id: String, name: String, description: String, importance: Int) =
        NotificationChannel(id, name, importance).apply {
            this.description = description
            setSound(null, null)
            enableVibration(false)
            setShowBadge(false)
        }
}
