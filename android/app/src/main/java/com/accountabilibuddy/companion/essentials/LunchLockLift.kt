package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.TokenVault
import java.time.Instant

/**
 * Lunch lifts the LockMyPhone-Extended lock that AccountabiliBuddy started for the focus session, and asks for it
 * again when lunch ends (Settings → Essentials → "Lift the lock for lunch").
 * - Only a lock AccountabiliBuddy itself asked for ([EssentialsLaunch.KEY_SESSION_LOCK_FOR] is that session's start) is
 *   ever stopped: never a lock started in LockMyPhone-Extended, never the night's.
 * - STOP_LOCK carries the Intent API token (Settings → Intent API there, "Allow stopping a lock by intent" on). It is
 *   kept sealed by [TokenVault] under its own key, never synced, never logged.
 * - One try per lunch: a missing token is shown as a note in the setting, never retried in a loop. A broadcast has no
 *   answer, so a refusal by LockMyPhone-Extended (wrong token, setting off) isn't seen here.
 */
object LunchLockLift {
    private const val TAG = "LunchLockLift"
    private const val PREFS = "lunchLockLift"
    private const val KEY_ON = "on"
    private const val KEY_TOKEN = "sealedToken"
    private const val KEY_LIFTED_FOR = "liftedFor"
    private const val KEY_NOTE = "note"

    const val NOTE_FAILED = "Couldn't lift the lock: check the token"

    fun enabled(context: Context): Boolean = prefs(context).getBoolean(KEY_ON, false)

    fun setEnabled(context: Context, on: Boolean) {
        prefs(context).edit().putBoolean(KEY_ON, on).apply()
    }

    fun hasToken(context: Context): Boolean = prefs(context).getString(KEY_TOKEN, null) != null

    /** Seals and keeps [token]; blank removes it. */
    fun saveToken(context: Context, token: String) {
        val clean = token.trim()
        val editor = prefs(context).edit().remove(KEY_NOTE)
        if (clean.isEmpty()) editor.remove(KEY_TOKEN) else editor.putString(KEY_TOKEN, TokenVault.seal(clean, TokenVault.LMPE_TOKEN_ALIAS))
        editor.apply()
    }

    /** The one-line note under the setting (null: nothing to say). */
    fun note(context: Context): String? = prefs(context).getString(KEY_NOTE, null)

    /** True while a lunch is on and lifting is set up, so the session's lock stays off (not asked for at unlocks). */
    fun liftingNow(context: Context, now: Instant): Boolean {
        val app = context.applicationContext as CompanionApp
        return enabled(context) && hasToken(context) && app.lunch.isOnLunch(now)
    }

    /** Lunch started or ended (or a refresh): lifts the session's lock once per lunch, asks for it again after. */
    @Synchronized
    fun check(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        val prefs = prefs(context)
        val lifted = prefs.getString(KEY_LIFTED_FOR, null)
        val launch = context.getSharedPreferences("essentials-launch", Context.MODE_PRIVATE)
        val lockedFor = launch.getString(EssentialsLaunch.KEY_SESSION_LOCK_FOR, null)
        val session = EssentialsLaunch.runningSessionStart(app, now)
        // No session of ours (or another one, or over): nothing to lift or bring back.
        if (session == null || session.first.toString() != lockedFor) {
            if (lifted != null) prefs.edit().remove(KEY_LIFTED_FOR).apply()
            return
        }

        val start = session.first.toString()
        val onLunch = app.lunch.isOnLunch(now)
        if (onLunch && lifted != start && enabled(context) && app.essentials.useLockMyPhone) {
            // One try, whatever comes of it.
            prefs.edit().putString(KEY_LIFTED_FOR, start).apply()
            if (stop(context)) prefs.edit().remove(KEY_NOTE).apply() else prefs.edit().putString(KEY_NOTE, NOTE_FAILED).apply()
        } else if (!onLunch && lifted == start) {
            prefs.edit().remove(KEY_LIFTED_FOR).apply()
            val profile = app.essentials.hardcoreProfile
            if (profile != null && app.essentials.useLockMyPhone && !EssentialsLaunch.sessionOnBreak(app, now)) {
                EssentialsLaunch.sendSessionLock(context, profile, session.second, now)
                Log.i(TAG, "lunch over: asked LockMyPhone-Extended for '$profile' again until ${session.second}")
            }
        }
    }

    /** "Test stop": ends a test lock started here; returns the note to show (also when nothing was sent, and why). */
    fun testStop(context: Context, now: Instant = Instant.now()): String {
        if (!hasToken(context)) return "Paste the token first."
        if (!EssentialsLaunch.testLockMayRun(context, now)) return "No test lock is running: start \"Try LockMyPhone-Extended's lock for 1 minute\" first."
        return if (stop(context)) "Asked LockMyPhone-Extended to stop the test lock. If it keeps running, check the token." else NOTE_FAILED
    }

    /** Sends STOP_LOCK with the stored token; false when there is no token or the broadcast failed. */
    private fun stop(context: Context): Boolean {
        val token = prefs(context).getString(KEY_TOKEN, null)?.let { TokenVault.open(it, TokenVault.LMPE_TOKEN_ALIAS) } ?: return false
        val intent = Intent(EssentialsLaunch.LMP_STOP).setPackage(EssentialsLaunch.LOCK_MY_PHONE).putExtra("token", token)
        val sent = runCatching { context.sendBroadcast(intent) }.isSuccess
        Log.i(TAG, "lunch: asked LockMyPhone-Extended to stop the session lock (sent $sent)")
        return sent
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
