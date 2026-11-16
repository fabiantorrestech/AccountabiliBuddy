package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.TrustedTime
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

    private const val KEY_SENT_AT = "heldSentAt"

    /** A phase with no set end is held this long at a time, and renewed when less than [HoldRules.RENEW_BEFORE] is left. */
    val LEASE: Duration = Duration.ofMinutes(30)

    /** True when the installed LockMyPhone-Extended has the hold. */
    fun holdCapable(context: Context): Boolean = LockMyPhoneClient.supports(context, LockMyPhoneClient.HOLD_LOCK)

    /** True while this app believes a hold of its own is in place (it keeps the strict path through one missed capabilities query). */
    fun holding(context: Context): Boolean = prefs(context).getString(KEY_HELD_FOR, null) != null

    /**
     * Holds, moves, renews or releases the session's hold to match now. All decisions are on trusted time ([TrustedTime]).
     * A hold already believed in place is sent again every [HoldRules.RESEND_AFTER] (and on every unlock, [resend]), so a
     * lock that died on LockMyPhone-Extended's side comes back at the next refresh or unlock.
     *
     * @param sessionGuarded false when nothing may hold the phone now, whatever runs; by default, unless the phone
     *   guard's pause (or "Pause in the car") is on, as for the older lock.
     * @param resend true from an unlock: the hold is sent again whatever was sent last.
     */
    @Synchronized
    fun reconcile(context: Context, now: Instant = TrustedTime.now(), sessionGuarded: Boolean? = null, resend: Boolean = false) {
        val app = context.applicationContext as CompanionApp
        val guarded = sessionGuarded ?: !app.pause.isQuiet
        val prefs = prefs(context)
        val heldFor = prefs.getString(KEY_HELD_FOR, null)
        // Not the lock (or not installed / not the real one): the hold is dropped. A missed capabilities query is not that (S3).
        if (!app.lockScreen.usesLockMyPhone(now)) {
            heldFor?.let { release(context, it) }
            return
        }

        val session = EssentialsLaunch.runningSessionStart(app, now)
        val free = freeReason(context, now)
        // PS6a: the profile is the own session's, else the global one; a ProductaBuddy own session whose profile names none holds nothing (B7b).
        val profile = EssentialsLaunch.lmpProfile(app, now)
        if (session == null || !guarded || !EssentialsLaunch.essentialsOnUnlock(app, now) || profile == null || free != null) {
            heldFor?.let {
                Log.i(TAG, "releasing '$it' (${free ?: if (session == null) "no session" else "off"})")
                release(context, it)
            }
            return
        }
        // No hold on offer (an LMP without it, or its answer is gone past the grace): give the hold up, so the older START_LOCK path or
        // the built-in screen takes over (hard rules: fail closed). One missed query is absorbed by the capabilities' grace.
        if (!holdCapable(context)) {
            Log.w(TAG, "no hold available: releasing '$heldFor'")
            heldFor?.let { release(context, it) }
            return
        }

        val holdId = session.first.toString()
        val untilMs = session.second?.toEpochMilli() ?: now.plus(LEASE).toEpochMilli()
        val heldUntil = prefs.getLong(KEY_HELD_UNTIL, 0L)
        if (heldFor != null && heldFor != holdId) release(context, heldFor)
        val due = HoldRules.due(heldFor, holdId, heldUntil, untilMs, session.second != null, prefs.getLong(KEY_SENT_AT, 0L), now, resend)
        if (!due) return

        if (LockMyPhoneClient.hold(context, holdId, profile, untilMs)) {
            prefs.edit().putString(KEY_HELD_FOR, holdId).putLong(KEY_HELD_UNTIL, untilMs).putLong(KEY_SENT_AT, now.toEpochMilli()).apply()
            Log.i(TAG, "holding '$holdId' with '$profile' until $untilMs")
        }
    }

    /** Why the phone is free of the session's lock right now, or null when it isn't. */
    fun freeReason(context: Context, now: Instant = TrustedTime.now()): String? {
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
            "refused" -> editor.putString(KEY_REFUSED_FOR, holdId).remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL).remove(KEY_SENT_AT)
            "started", "held_by_other" -> editor.remove(KEY_REFUSED_FOR)
            // Over on LockMyPhone-Extended's side: the next reconcile holds again if the session still wants it.
            "ended", "released" -> if (prefs(context).getString(KEY_HELD_FOR, null) == holdId) editor.remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL).remove(KEY_SENT_AT)
        }
        editor.apply()
        Log.i(TAG, "LockMyPhone-Extended: '$holdId' $event${lockName?.let { " ($it)" } ?: ""}")
    }

    /**
     * True when LockMyPhone-Extended is the lock for this session right now: in use, and (with the hold) its hold
     * wasn't refused. False means the built-in essentials screen stands in (fail closed).
     */
    fun lockMyPhoneLocks(context: Context, now: Instant = TrustedTime.now()): Boolean {
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
        prefs(context).edit().remove(KEY_HELD_FOR).remove(KEY_HELD_UNTIL).remove(KEY_SENT_AT).apply()
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
