package com.accountabilibuddy.watch.timers

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.VibrationEffect
import android.os.VibratorManager
import com.accountabilibuddy.shared.PhaseAlert
import com.accountabilibuddy.watch.alerts.AlertSettings
import com.accountabilibuddy.watch.alerts.PhaseAlertReceiver
import com.accountabilibuddy.watch.alerts.Vibration
import java.time.Instant

/**
 * A timer's alarm went off: the phase-end buzz (at the strength and with the sound chosen on the Alerts page; a default
 * buzz when both are off, since a timer never ends silently), the
 * "Time's up" notification that opens the Timers page, and the complication moves on to the next timer.
 */
class TimerEndReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val id = intent.getIntExtra(ID, -1)
        val timer = TimerStore.timers(context).value.firstOrNull { it.id == id } ?: return
        // Paused or given more time since the alarm was set: not its end (a second early is the alarm's own slack).
        if (!timer.isDone(Instant.now().plusSeconds(1))) {
            return
        }

        // A timer is its own alarm: it never ends silently, even with the Alerts page's vibration and sound both off.
        if (AlertSettings.vibration(context) == Vibration.Off && !AlertSettings.sound(context)) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator
                ?.vibrate(VibrationEffect.createWaveform(DEFAULT_BUZZ, -1))
        } else {
            PhaseAlertReceiver.alert(context, PhaseAlert.Kind.End)
        }
        TimerRing.show(context, timer)
        TimerStore.refresh(context)
    }

    companion object {
        const val ID = "timerId"

        // The medium end buzz's rhythm (off, on, off, on ms) at the default amplitude.
        private val DEFAULT_BUZZ = longArrayOf(0, 300, 150, 300)
    }
}
