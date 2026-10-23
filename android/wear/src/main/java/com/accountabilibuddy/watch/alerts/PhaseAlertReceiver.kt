package com.accountabilibuddy.watch.alerts

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.media.RingtoneManager
import android.os.VibrationEffect
import android.os.VibratorManager
import com.accountabilibuddy.shared.PhaseAlert

/** A phase's alert is due: buzz at the chosen strength (shorter for the one-minute warning), with the sound when it's on. */
class PhaseAlertReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val kind = runCatching { PhaseAlert.Kind.valueOf(intent.getStringExtra(KIND) ?: return) }.getOrNull() ?: return
        val strength = AlertSettings.vibration(context)
        if (strength != Vibration.Off) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator?.vibrate(effect(strength, kind))
        }

        if (kind != PhaseAlert.Kind.Warning && AlertSettings.sound(context)) {
            runCatching { RingtoneManager.getRingtone(context, RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION))?.play() }
        }
    }

    // Pulses (on ms, amplitude): one short pulse for the warning; for the end, more and longer with the strength.
    private fun effect(strength: Vibration, kind: PhaseAlert.Kind): VibrationEffect {
        val amplitude = when (strength) {
            Vibration.Light -> 90
            Vibration.Medium -> 170
            else -> 255
        }
        if (kind == PhaseAlert.Kind.Warning) {
            return VibrationEffect.createOneShot(120, amplitude)
        }

        val (pulses, length) = when (strength) {
            Vibration.Light -> 1 to 200L
            Vibration.Medium -> 2 to 300L
            else -> 3 to 450L
        }
        val timings = LongArray(pulses * 2) { index -> if (index % 2 == 0) 150L else length }.also { it[0] = 0L }
        val amplitudes = IntArray(pulses * 2) { index -> if (index % 2 == 0) 0 else amplitude }
        return VibrationEffect.createWaveform(timings, amplitudes, -1)
    }

    companion object {
        const val KIND = "kind"
        const val ON_BREAK = "onBreak"
    }
}
