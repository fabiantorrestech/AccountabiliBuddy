package com.accountabilibuddy.watch.alerts

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.shared.PhaseAlert
import com.accountabilibuddy.shared.PhaseAlerts
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.watch.link.SerialBackground
import java.time.Instant

/**
 * Sets the watch's buzzes for the running phase ([PhaseAlerts]) each time a snapshot arrives or the alert choices
 * change: at the phase's end and a minute before, at the exact time (the app may use exact alarms: it's a timer), and
 * when planned work starts with no session running (the start nudge; a session starting first cancels it).
 * The buzz then comes even when the phone is out of reach.
 */
object PhaseAlertScheduler {
    private val background = SerialBackground("phase-alerts")

    // The alarms set last (kind, time, on a break), on the background thread only; null until the first run after the
    // process started, so that run always sets them. Equal wishes are not set again (the phone resends every few minutes).
    private var scheduled: List<Triple<PhaseAlert.Kind, Instant, Boolean>>? = null

    /** Sets the alarms for [snapshot] and the alert choices: on a background thread, in the order asked. */
    fun schedule(context: Context, snapshot: WatchSnapshot?) {
        val app = context.applicationContext
        background.run { scheduleNow(app, snapshot) }
    }

    private fun scheduleNow(context: Context, snapshot: WatchSnapshot?) {
        val alarms = context.getSystemService(AlarmManager::class.java) ?: return
        val now = Instant.now()
        val silent = AlertSettings.vibration(context) == Vibration.Off && !AlertSettings.sound(context)
        val alerts = if (silent) {
            emptyList()
        } else {
            PhaseAlerts.due(snapshot?.running, now, AlertSettings.warning(context)) +
                listOfNotNull(PhaseAlerts.goal(snapshot, now, java.time.ZoneId.systemDefault()), PhaseAlerts.workStart(snapshot, now))
        }
        val wanted = alerts.map { Triple(it.kind, it.at, it.onBreak) }
        if (wanted == scheduled) {
            return
        }

        scheduled = wanted
        PhaseAlert.Kind.entries.forEach { kind -> alarms.cancel(intent(context, kind, onBreak = false)) }
        for (alert in alerts) {
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
