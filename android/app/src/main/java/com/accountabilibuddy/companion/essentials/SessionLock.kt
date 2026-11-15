package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import java.time.Duration
import java.time.Instant

/**
 * The focus session's hold on LockMyPhone-Extended (ADR 73, LMP ADR 0011). While a session's focus phase runs with
 * LockMyPhone-Extended as the lock and "During focus sessions" on, this app holds the phone locked until the phase's
 * end: no early unlock, no NFC unlock, no Limited style, this app always allowed. A phase with no set end gets a
 * [LEASE] that is renewed while it runs, so a stopped app can never keep the phone locked.
 *
 * The phone is left free (the hold released, only this app's own) during:
 * - a break;
 * - a lunch, when "Lift the lock at lunch" is on;
 * - emergency phone time;
 * - "Need it for work".
 *
 * The hold comes back at the next refresh or unlock after any of these ends.
 *
 * [reconcile] is the one entry: the refresher, every unlock, the emergency and work-use screens and LockMyPhone-Extended's
 * replies call it. An LockMyPhone-Extended without the hold keeps the older START_LOCK path in [EssentialsLaunch].
 */
object SessionLock {
    private const val TAG = "SessionLock"
    private const val PREFS = "session-lock"
    private const val KEY_HELD_FOR = "heldFor"
    private const val KEY_HELD_UNTIL = "heldUntil"
    private const val KEY_REFUSED_FOR = "refusedFor"
    private const val KEY_LAST_EVENT = "lastEvent"
    private const val KEY_LAST_LOCK = "lastLock"

    /** A phase with no set end is held this long at a time, and renewed when less than [RENEW_BEFORE] is left. */
    val LEASE: Duration = Duration.ofMinutes(30)
    private val RENEW_BEFORE: Duration = Duration.ofMinutes(15)

    /** True when the installed LockMyPhone-Extended has the hold. */
    fun holdCapable(context: Context): Boolean = LockMyPhoneClient.supports(context, LockMyPhoneClient.HOLD_LOCK)

    /**
     * Holds, moves, renews or releases the session's hold to match now.
     *
     * @param sessionGuarded false when nothing may hold the phone now, whatever runs; by default, unless the phone
     *   guard's pause (or "Pause in the car") is on, as for the older lock.
     */
    @Synchronized
    fun reconcile(context: Context, now: Instant = Instant.now(), sessionGuarded: Boolean? = null) {
        val app = context.applicationContext as CompanionApp
        val guarded = sessionGuarded ?: !app.pause.isQuiet
        val prefs = prefs(context)
        val heldFor = prefs.getString(KEY_HELD_FOR, null)
        if (!app.lockScreen.usesLockMyPhone(now) || !holdCapable(context)) {
            heldFor?.let { release(context, it) }
            return
        }

        val session = EssentialsLaunch.runningSessionStart(app, now)
        val free = freeReason(context, now)
        if (session == null || !guarded || !app.essentials.onUnlockInSession || free != null) {
            heldFor?.let {
                Log.i(TAG, "releasing '$it' (${free ?: if (session == null) "no session" else "off"})")
                release(context, it)
            }
            return
        }

        val holdId = session.first.toString()
        val profile = app.essentials.hardcoreProfile
        val untilMs = session.second?.toEpochMilli() ?: now.plus(LEASE).toEpochMilli()
        val heldUntil = prefs.getLong(KEY_HELD_UNTIL, 0L)
        if (heldFor != null && heldFor != holdId) release(context, heldFor)
        val due = heldFor != holdId ||
            (session.second != null && heldUntil != untilMs) ||
            (session.second == null && heldUntil - now.toEpochMilli() < RENEW_BEFORE.toMillis())
        if (!due) return

        if (LockMyPhoneClient.hold(context, holdId, profile, untilMs)) {
            prefs.edit().putString(KEY_HELD_FOR, holdId).putLong(KEY_HELD_UNTIL, untilMs).apply()
            Log.i(TAG, "holding '$holdId' with '$profile' until $untilMs")
        }
    }

    /** Why the phone is free of the session's lock right now, or null when it isn't. */
    fun freeReason(context: Context, now: Instant = Instant.now()): String? {
        val app = context.applicationContext as CompanionApp
        return when {
            app.emergency.isActive(now) -> "emergency phone time"
            app.workUse.active(now) != null -> "need it for work"
            LunchLockLift.enabled(context) && app.lunch.isOnLunch(now) -> "lunch"
            EssentialsLaunch.sessionOnBreak(app, now) -> "break"
            else -> null
        }
    }

    /** LockMyPhone-Extended's reply ([LockResultReceiver]): remembered for Settings, and a refusal falls back to the built-in screen. */
    @Synchronized
    fun onResult(context: Context, holdId: String, event: String, lockName: String?) {
        val editor = prefs(context).edit().putString(KEY_LAST_EVENT, event).putString(KEY_LAST_LOCK, lockName)
        when (event) {
            // The built-in screen stands in; the next refresh asks again (the profile or device admin may be fixed by then).
            "refused" -> editor.putString(KEY_REFUSED_FOR, holdId).remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL)
            "started", "held_by_other" -> editor.remove(KEY_REFUSED_FOR)
            // Over on LockMyPhone-Extended's side: the next reconcile holds again if the session still wants it.
            "ended", "released" -> if (prefs(context).getString(KEY_HELD_FOR, null) == holdId) editor.remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL)
        }
        editor.apply()
        Log.i(TAG, "LockMyPhone-Extended: '$holdId' $event${lockName?.let { " ($it)" } ?: ""}")
    }

    /**
     * True when LockMyPhone-Extended is the lock for this session right now: in use, and (with the hold) its hold
     * wasn't refused. False means the built-in essentials screen stands in (fail closed).
     */
    fun lockMyPhoneLocks(context: Context, now: Instant = Instant.now()): Boolean {
        val app = context.applicationContext as CompanionApp
        if (!app.lockScreen.usesLockMyPhone(now)) return false
        if (!holdCapable(context)) return true
        val holdId = EssentialsLaunch.runningSessionStart(app, now)?.first?.toString() ?: return true
        return prefs(context).getString(KEY_REFUSED_FOR, null) != holdId
    }

    /** The last reply, for Settings: the event and the lock that runs (e.g. a stronger lock of yours under the hold). */
    fun lastReply(context: Context): Pair<String, String?>? =
        prefs(context).getString(KEY_LAST_EVENT, null)?.let { it to prefs(context).getString(KEY_LAST_LOCK, null) }

    private fun release(context: Context, holdId: String) {
        LockMyPhoneClient.release(context, holdId)
        prefs(context).edit().remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL).apply()
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
