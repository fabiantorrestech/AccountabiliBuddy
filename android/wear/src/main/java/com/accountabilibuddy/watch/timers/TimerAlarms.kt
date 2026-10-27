package com.accountabilibuddy.watch.timers

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import java.time.Instant

/**
 * One exact alarm per running timer at its end (like the phase alerts: exact even in Doze, so it buzzes with the screen
 * off). Paused, done and dismissed timers have none. The ids of the alarms set are kept so a gone timer's is cancelled.
 */
object TimerAlarms {
    private const val PREFS = "timers"
    private const val SCHEDULED = "scheduled"

    fun schedule(context: Context, timers: List<WatchTimer>) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        prefs.getStringSet(SCHEDULED, emptySet()).orEmpty().mapNotNull(String::toIntOrNull).forEach { id ->
            alarms.cancel(intent(context, id))
        }

        val now = Instant.now()
        val counting = timers.filter { it.isCounting(now) }
        for (timer in counting) {
            val at = timer.endsAt!!.toEpochMilli()
            val pending = intent(context, timer.id)
            if (alarms.canScheduleExactAlarms()) {
                alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
            } else {
                alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
            }
        }
        prefs.edit().putStringSet(SCHEDULED, counting.map { it.id.toString() }.toSet()).apply()
    }

    private fun intent(context: Context, id: Int): PendingIntent =
        PendingIntent.getBroadcast(
            context, REQUEST_BASE + id,
            Intent(context, TimerEndReceiver::class.java).putExtra(TimerEndReceiver.ID, id),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

    // Apart from the phase alerts' request codes (0-3).
    private const val REQUEST_BASE = 1_000
}
