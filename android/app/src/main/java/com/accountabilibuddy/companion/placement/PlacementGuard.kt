package com.accountabilibuddy.companion.placement

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import java.time.Duration
import java.time.Instant

/**
 * The on/off switch for the phone placement guard (Settings → Essentials → Phone placement), kept in preferences
 * "placement" and never synced. On by default. Off means no sensors, no placement in the sync file and no essentials
 * escalation from placement.
 *
 * Hard rules: turning it on applies at once; turning it off is a looser change, so it is refused while a focus session
 * runs and otherwise waits [OFF_WAIT]. A pending off is cancelled by a session starting before it is due, or by turning
 * the guard back on. The wait is applied lazily whenever the switch is read, so it also holds across a restart.
 */
class PlacementGuard(context: Context) {
    private val app = context.applicationContext as CompanionApp
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Whether the guard is on right now (a pending off still counts as on until it is due). */
    @Synchronized
    fun isOn(now: Instant): Boolean {
        settle(now)
        return prefs.getBoolean(KEY_ON, true)
    }

    /** When a pending off is due; null when none is waiting. */
    @Synchronized
    fun pendingOffAt(now: Instant): Instant? {
        settle(now)
        return prefs.getLong(KEY_OFF_AT, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
    }

    /** True while a focus session runs: the switch can't be turned off then. */
    fun locked(now: Instant): Boolean = PlacementSession.current(app, now) != null

    /** Turning on applies at once and cancels a pending off. */
    @Synchronized
    fun turnOn() {
        prefs.edit().putBoolean(KEY_ON, true).remove(KEY_OFF_AT).apply()
    }

    /** Starts the wait for turning off; false (nothing changes) while a focus session runs. */
    @Synchronized
    fun requestOff(now: Instant): Boolean {
        if (locked(now)) {
            return false
        }

        if (prefs.getBoolean(KEY_ON, true) && prefs.getLong(KEY_OFF_AT, 0L) == 0L) {
            prefs.edit().putLong(KEY_OFF_AT, now.plus(OFF_WAIT).toEpochMilli()).apply()
        }

        return true
    }

    // Cancels a pending off when a session runs; applies it when due.
    private fun settle(now: Instant) {
        val offAt = prefs.getLong(KEY_OFF_AT, 0L)
        if (offAt == 0L) {
            return
        }

        if (locked(now)) {
            prefs.edit().remove(KEY_OFF_AT).apply()
        } else if (now.toEpochMilli() >= offAt) {
            prefs.edit().putBoolean(KEY_ON, false).remove(KEY_OFF_AT).apply()
        }
    }

    private companion object {
        val OFF_WAIT: Duration = Duration.ofMinutes(10)
        const val PREFS = "placement"
        const val KEY_ON = "guardOn"
        const val KEY_OFF_AT = "guardOffAt"
    }
}
