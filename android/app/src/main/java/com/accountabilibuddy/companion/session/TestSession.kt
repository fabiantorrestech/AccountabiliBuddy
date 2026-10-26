package com.accountabilibuddy.companion.session

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant

/**
 * A test session on the phone (Today → More options): for trying the phone's own reactions to a session on another
 * device (the essentials screen on unlock, the LockMyPhone-Extended hardcore profile, Curbox hard mode, the timeline
 * notification) without a real one. 1 minute by default, 1-60 minutes, ends early at once (no wait, no reason).
 * Never counted: it isn't in History, progress, the sync file or the watch snapshot; only
 * `EssentialsLaunch.runningSessionStart` and the phone's header read it, and a real session on another device wins.
 * Preferences "testSession": start, end.
 */
class TestSession(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _window = MutableStateFlow(load())

    /** The last test started (start, end); it may be over. */
    val window: StateFlow<Pair<Instant, Instant>?> = _window.asStateFlow()

    /** Called after a test starts or ends early (the phone's reactions are checked again). */
    var onChanged: (() -> Unit)? = null

    /** The running test (start, end); null when none runs. */
    fun running(now: Instant = Instant.now()): Pair<Instant, Instant>? =
        _window.value?.takeIf { (start, end) -> !now.isBefore(start) && now.isBefore(end) }

    /** The test length last chosen, in minutes (1 by default). */
    var minutes: Int
        get() = prefs.getInt(KEY_MINUTES, DEFAULT_MINUTES).coerceIn(MIN_MINUTES, MAX_MINUTES)
        set(value) = prefs.edit().putInt(KEY_MINUTES, value.coerceIn(MIN_MINUTES, MAX_MINUTES)).apply()

    @Synchronized
    fun start(minutes: Int, now: Instant = Instant.now()) {
        val length = minutes.coerceIn(MIN_MINUTES, MAX_MINUTES)
        save(now, now.plus(Duration.ofMinutes(length.toLong())))
        onChanged?.invoke()
    }

    /** Ends the running test now (no wait: it's a test). */
    @Synchronized
    fun endNow(now: Instant = Instant.now()) {
        val (start, _) = running(now) ?: return
        save(start, now)
        onChanged?.invoke()
    }

    private fun save(start: Instant, end: Instant) {
        prefs.edit().putLong(KEY_START, start.toEpochMilli()).putLong(KEY_END, end.toEpochMilli()).apply()
        _window.value = start to end
    }

    private fun load(): Pair<Instant, Instant>? {
        val start = prefs.getLong(KEY_START, 0L).takeIf { it > 0 } ?: return null
        val end = prefs.getLong(KEY_END, 0L).takeIf { it > 0 } ?: return null
        return Instant.ofEpochMilli(start) to Instant.ofEpochMilli(end)
    }

    companion object {
        private const val PREFS = "testSession"
        private const val KEY_START = "start"
        private const val KEY_END = "end"
        private const val KEY_MINUTES = "minutes"
        const val DEFAULT_MINUTES = 1
        const val MIN_MINUTES = 1
        const val MAX_MINUTES = 60
    }
}
