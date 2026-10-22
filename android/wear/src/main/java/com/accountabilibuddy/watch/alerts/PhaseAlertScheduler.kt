package com.accountabilibuddy.watch.alerts

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.shared.PhaseAlert
import com.accountabilibuddy.shared.PhaseAlerts
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Instant

/**
 * Sets the watch's buzzes for the running phase ([PhaseAlerts]) each time a snapshot arrives or the alert choices
 * change: at the phase's end and a minute before, at the exact time (the app may use exact alarms: it's a timer).
 * The buzz then comes even when the phone is out of reach.
 */
object PhaseAlertScheduler {
    fun schedule(context: Context, snapshot: WatchSnapshot?) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        PhaseAlert.Kind.entries.forEach { kind -> alarms.cancel(intent(context, kind, onBreak = false)) }
        if (AlertSettings.vibration(context) == Vibration.Off && !AlertSettings.sound(context)) {
            return
        }

        for (alert in PhaseAlerts.due(snapshot?.running, Instant.now(), AlertSettings.warning(context))) {
            val pending = intent(context, alert.kind, alert.onBreak)
            val at = alert.at.toEpochMilli()
            if (alarms.canScheduleExactAlarms()) {
                alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
            } else {
                alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
            }
        }
    }

    // One pending alarm per kind (the newest phase replaces it); whether it's a break rides along.
    private fun intent(context: Context, kind: PhaseAlert.Kind, onBreak: Boolean): PendingIntent =
        PendingIntent.getBroadcast(
            context, kind.ordinal,
            Intent(context, PhaseAlertReceiver::class.java).putExtra(PhaseAlertReceiver.KIND, kind.name).putExtra(PhaseAlertReceiver.ON_BREAK, onBreak),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
}
