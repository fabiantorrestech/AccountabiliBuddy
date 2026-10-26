package com.accountabilibuddy.companion.curbox

import android.content.Context
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import java.time.Duration
import java.time.Instant

/**
 * Curbox hard mode (optional, off by default): for each focus session on another device the phone starts a chosen
 * Curbox focus group (one made with exit off, so it can't be ended in Curbox) for the session's time left, and ends
 * it when the session ends early or the phone's emergency time starts. Curbox enforces; this only asks it through
 * its API (START_FOCUS, STOP_FOCUS). The phone's test session counts too (`EssentialsLaunch.runningSessionStart`).
 * Preferences "curbox".
 */
object CurboxHardMode {
    private const val TAG = "CurboxHardMode"
    private const val PREFS = "curbox"
    private const val KEY_ON = "hardMode"
    private const val KEY_GROUP = "focusGroup"
    private const val KEY_STARTED_FOR = "startedFor"
    private const val KEY_ENDS_AT = "endsAt"

    /** A session with no set end gets this much; the next one starts it again. */
    private const val OPEN_SESSION_MINUTES = 60

    fun enabled(context: Context): Boolean = prefs(context).getBoolean(KEY_ON, false)

    fun setEnabled(context: Context, on: Boolean) = prefs(context).edit().putBoolean(KEY_ON, on).apply()

    fun group(context: Context): String? = prefs(context).getString(KEY_GROUP, null)

    fun setGroup(context: Context, id: String?) = prefs(context).edit().putString(KEY_GROUP, id).apply()

    /** On each refresh: start for a new session, end when it ended early or emergency time began. */
    suspend fun check(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        val prefs = prefs(context)
        val startedFor = prefs.getString(KEY_STARTED_FOR, null)
        val endsAt = prefs.getLong(KEY_ENDS_AT, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
        val ours = startedFor != null && endsAt?.isAfter(now) == true
        val curbox = CurboxClient(context)
        val running = EssentialsLaunch.runningSessionStart(app, now)

        if (ours && (running == null || running.first.toString() != startedFor || app.emergency.isActive(now))) {
            if (curbox.stopFocus()) Log.i(TAG, "stopped (session over or emergency time)")
            prefs.edit().remove(KEY_STARTED_FOR).remove(KEY_ENDS_AT).apply()
            return
        }

        val group = group(context)
        if (!enabled(context) || group == null || running == null || app.emergency.isActive(now) || running.first.toString() == startedFor) {
            return
        }

        // Rounded up, so a 1-minute test session asked a moment after it started still gets its minute.
        val minutes = running.second?.let { EssentialsLaunch.ceilMinutes(Duration.between(now, it)).toInt() } ?: OPEN_SESSION_MINUTES
        if (minutes <= 0) return
        if (curbox.startFocus(group, minutes)) {
            prefs.edit()
                .putString(KEY_STARTED_FOR, running.first.toString())
                .putLong(KEY_ENDS_AT, now.plus(Duration.ofMinutes(minutes.toLong())).toEpochMilli())
                .apply()
            Log.i(TAG, "started focus group for $minutes min")
        }
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
