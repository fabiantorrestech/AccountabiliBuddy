package com.accountabilibuddy.companion.essentials

import android.content.SharedPreferences
import java.time.Instant

/**
 * One looser change waiting out [LooserChangeRules.WAIT] (stored in [prefs] under "pending.<name>"), so it holds across a
 * restart. The caller applies the value [settle] returns; times are the caller's trusted time.
 */
class DelayedSetting(private val prefs: SharedPreferences, name: String) {
    private val valueKey = "pending.$name"
    private val atKey = "pendingAt.$name"

    /** The waiting value and when it is due; null when nothing waits. */
    fun pending(): Pair<String, Instant>? {
        val at = prefs.getLong(atKey, 0L).takeIf { it > 0 } ?: return null
        val value = prefs.getString(valueKey, null) ?: return null
        return value to Instant.ofEpochMilli(at)
    }

    /** Starts waiting for [value]; the same value already waiting keeps its due time. */
    fun schedule(value: String, now: Instant) {
        if (pending()?.first == value) return
        prefs.edit().putString(valueKey, value).putLong(atKey, LooserChangeRules.dueAt(now).toEpochMilli()).apply()
    }

    fun cancel() {
        prefs.edit().remove(valueKey).remove(atKey).apply()
    }

    /** A session cancels the waiting change; when it is due it is cleared and its value returned (apply it). Null otherwise. */
    fun settle(sessionRunning: Boolean, now: Instant): String? {
        val (value, due) = pending() ?: return null
        return when (LooserChangeRules.settle(due.toEpochMilli(), sessionRunning, now)) {
            LooserChangeRules.Settle.Cancel -> { cancel(); null }
            LooserChangeRules.Settle.Apply -> { cancel(); value }
            LooserChangeRules.Settle.Keep -> null
        }
    }
}
