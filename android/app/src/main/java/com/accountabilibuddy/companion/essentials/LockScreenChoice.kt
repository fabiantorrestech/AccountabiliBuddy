package com.accountabilibuddy.companion.essentials

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.TrustedTime
import java.time.Duration
import java.time.Instant

/** How the phone keeps you to your essentials (ADR 73). */
enum class LockScreen {
    /** LockMyPhone-Extended locks the phone; the built-in screen never shows. */
    LockMyPhone,

    /** This app's own essentials screen (soft: nothing is blocked). */
    BuiltIn,
}

/**
 * The one global choice, "How your phone locks" (ADR 73), kept in the focus essentials' preferences and never
 * synced. With no choice made, LockMyPhone-Extended when it is installed, else the built-in screen.
 *
 * Hard rules: choosing LockMyPhone-Extended applies at once. Choosing the built-in screen while LockMyPhone-Extended
 * is in use is a looser change: refused while a session runs, otherwise it waits [BUILT_IN_WAIT]. A session starting
 * first cancels it. Like [com.accountabilibuddy.companion.placement.PlacementGuard], the wait is applied whenever the
 * choice is read, so it holds across a restart.
 */
class LockScreenChoice(context: Context) {
    private val app = context.applicationContext as CompanionApp
    private val prefs = context.getSharedPreferences(EssentialsKind.Focus.prefs, Context.MODE_PRIVATE)

    /** The choice right now (a pending switch to the built-in screen still counts as LockMyPhone-Extended until due). */
    @Synchronized
    fun chosen(now: Instant = TrustedTime.now()): LockScreen {
        settle(now)
        return stored() ?: defaultChoice()
    }

    /**
     * True when LockMyPhone-Extended is the lock right now: chosen and installed. When it isn't installed, the built-in
     * screen is used whatever was chosen (fail closed: never no screen in a session).
     */
    fun usesLockMyPhone(now: Instant = TrustedTime.now()): Boolean =
        chosen(now) == LockScreen.LockMyPhone && EssentialsLaunch.lockMyPhoneInstalled(app)

    /** When a pending switch to the built-in screen is due; null when none is waiting. */
    @Synchronized
    fun pendingBuiltInAt(now: Instant = TrustedTime.now()): Instant? {
        settle(now)
        return prefs.getLong(KEY_BUILT_IN_AT, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
    }

    /** True while a session runs: the choice can't be loosened then. */
    fun locked(now: Instant = TrustedTime.now()): Boolean = EssentialsLaunch.runningSessionStart(app, now) != null

    /** LockMyPhone-Extended: applies at once and cancels a pending switch. */
    @Synchronized
    fun chooseLockMyPhone() {
        prefs.edit().putString(KEY_CHOICE, LockScreen.LockMyPhone.name).remove(KEY_BUILT_IN_AT).apply()
        app.essentials.resync()
        app.bedtimeEssentials.resync()
    }

    /**
     * The built-in screen: at once when LockMyPhone-Extended isn't in use anyway, else it waits. False (nothing changes)
     * while a session runs.
     */
    @Synchronized
    fun requestBuiltIn(now: Instant = TrustedTime.now()): Boolean {
        if (!usesLockMyPhone(now)) {
            prefs.edit().putString(KEY_CHOICE, LockScreen.BuiltIn.name).remove(KEY_BUILT_IN_AT).apply()
            return true
        }
        if (locked(now)) return false
        if (prefs.getLong(KEY_BUILT_IN_AT, 0L) == 0L) {
            prefs.edit().putLong(KEY_BUILT_IN_AT, now.plus(BUILT_IN_WAIT).toEpochMilli()).apply()
        }
        return true
    }

    private fun stored(): LockScreen? =
        prefs.getString(KEY_CHOICE, null)?.let { name -> LockScreen.entries.firstOrNull { it.name == name } }
            // Before ADR 73: the old "Use LockMyPhone-Extended" switch, when it was turned off, means the built-in screen.
            ?: LockScreen.BuiltIn.takeIf { prefs.contains(KEY_LEGACY_USE_LMP) && !prefs.getBoolean(KEY_LEGACY_USE_LMP, true) }

    private fun defaultChoice(): LockScreen =
        if (EssentialsLaunch.lockMyPhoneInstalled(app)) LockScreen.LockMyPhone else LockScreen.BuiltIn

    // Cancels a pending switch when a session runs; applies it when due.
    private fun settle(now: Instant) {
        val dueAt = prefs.getLong(KEY_BUILT_IN_AT, 0L)
        if (dueAt == 0L) return
        if (locked(now)) {
            prefs.edit().remove(KEY_BUILT_IN_AT).apply()
        } else if (now.toEpochMilli() >= dueAt) {
            prefs.edit().putString(KEY_CHOICE, LockScreen.BuiltIn.name).remove(KEY_BUILT_IN_AT).apply()
        }
    }

    companion object {
        /** How long switching from LockMyPhone-Extended to the built-in screen waits (the looser-change wait). */
        val BUILT_IN_WAIT: Duration = LooserChangeRules.WAIT
        private const val KEY_CHOICE = "lockScreen"
        private const val KEY_BUILT_IN_AT = "lockScreenBuiltInAt"
        private const val KEY_LEGACY_USE_LMP = "useLockMyPhone"
    }
}
