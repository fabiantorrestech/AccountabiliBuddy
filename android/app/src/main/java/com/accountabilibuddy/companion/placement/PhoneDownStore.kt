package com.accountabilibuddy.companion.placement

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import java.time.Duration
import java.time.Instant

/**
 * What the "Phone down" screen keeps on this phone (preferences "phoneDown", never synced): the two pocket penalty
 * switches ([PocketPenalty], Settings → Essentials → Phone placement) and a short log of when the screen was shown.
 *
 * The switches follow the placement switch's hard rules ([PlacementGuard]): turning one on applies at once; turning
 * one off is a looser change, so it is refused while a focus session runs and otherwise waits [OFF_WAIT]. A pending
 * off is cancelled by a session starting before it is due, or by turning the switch back on. The wait is applied
 * lazily whenever a switch is read, so it also holds across a restart.
 *
 * The log lets unlocks that showed the screen count as pickups even when they went on to an essential app (Essentials /
 * quick reply): with the guard on, only a call is exempt.
 */
class PhoneDownStore(context: Context) {
    private val app = context.applicationContext as CompanionApp
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Whether [penalty] is on right now (a pending off still counts as on until it is due). */
    @Synchronized
    fun isOn(penalty: PocketPenalty, now: Instant): Boolean {
        settle(penalty, now)
        return prefs.getBoolean(penalty.keyOn, true)
    }

    /** When a pending off of [penalty] is due; null when none is waiting. */
    @Synchronized
    fun pendingOffAt(penalty: PocketPenalty, now: Instant): Instant? {
        settle(penalty, now)
        return prefs.getLong(penalty.keyOffAt, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
    }

    /** True while a focus session runs: no switch can be turned off then. */
    fun locked(now: Instant): Boolean = app.placementGuard.locked(now)

    /** Turning on applies at once and cancels a pending off. */
    @Synchronized
    fun turnOn(penalty: PocketPenalty) {
        prefs.edit().putBoolean(penalty.keyOn, true).remove(penalty.keyOffAt).apply()
    }

    /** Starts the wait for turning [penalty] off; false (nothing changes) while a focus session runs. */
    @Synchronized
    fun requestOff(penalty: PocketPenalty, now: Instant): Boolean {
        if (locked(now)) {
            return false
        }

        if (prefs.getBoolean(penalty.keyOn, true) && prefs.getLong(penalty.keyOffAt, 0L) == 0L) {
            prefs.edit().putLong(penalty.keyOffAt, now.plus(OFF_WAIT).toEpochMilli()).apply()
        }

        return true
    }

    /** The screen was shown for an unlock at [now]. */
    @Synchronized
    fun recordShown(now: Instant) {
        val cutoff = now.minus(KEEP_LOG).toEpochMilli()
        val kept = shownTimes().filter { it >= cutoff }.takeLast(MAX_LOG - 1) + now.toEpochMilli()
        prefs.edit().putString(KEY_SHOWN, kept.joinToString(",")).apply()
    }

    /** True when the screen was shown for the unlock at [unlock] (the screen opens a moment after it). */
    @Synchronized
    fun shownFor(unlock: Instant): Boolean {
        val from = unlock.minus(SHOWN_BEFORE).toEpochMilli()
        val to = unlock.plus(SHOWN_AFTER).toEpochMilli()
        return shownTimes().any { it in from..to }
    }

    // Cancels a pending off when a session runs; applies it when due.
    private fun settle(penalty: PocketPenalty, now: Instant) {
        val offAt = prefs.getLong(penalty.keyOffAt, 0L)
        if (offAt == 0L) {
            return
        }

        if (locked(now)) {
            prefs.edit().remove(penalty.keyOffAt).apply()
        } else if (now.toEpochMilli() >= offAt) {
            prefs.edit().putBoolean(penalty.keyOn, false).remove(penalty.keyOffAt).apply()
        }
    }

    private fun shownTimes(): List<Long> =
        prefs.getString(KEY_SHOWN, null)?.split(',')?.mapNotNull { it.toLongOrNull() } ?: emptyList()

    private companion object {
        val OFF_WAIT: Duration = Duration.ofMinutes(10)
        val KEEP_LOG: Duration = Duration.ofDays(14)
        val SHOWN_BEFORE: Duration = Duration.ofSeconds(2)
        val SHOWN_AFTER: Duration = Duration.ofSeconds(15)
        const val MAX_LOG = 500
        const val PREFS = "phoneDown"
        const val KEY_SHOWN = "shown"
    }
}
