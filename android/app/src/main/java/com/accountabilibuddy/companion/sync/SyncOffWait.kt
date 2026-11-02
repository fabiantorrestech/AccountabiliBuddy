package com.accountabilibuddy.companion.sync

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant

/**
 * The wait for turning off the last way of syncing (the pattern of `SleepSettingsSync` and `PlacementGuard`, a looser change): the user asks,
 * the way stays on and [pending] says when it turns off; [SyncCoordinator] applies it when the time has come and no focus session runs
 * (a wait that ends in a session applies after it). Kept in prefs "syncOff" (never synced), so a restart does not skip it. Cancelling works
 * at any time. There is no "apply now" and nothing shortens the wait.
 */
class SyncOffWait(context: Context) {
    private val prefs = context.getSharedPreferences("syncOff", Context.MODE_PRIVATE)
    private val _pending = MutableStateFlow(load())

    /** The turn-off that is waiting, or null. */
    val pending: StateFlow<PendingSyncOff?> = _pending.asStateFlow()

    /** Called when a wait starts, so the app can apply it when it is due. */
    var onStart: (() -> Unit)? = null

    /** Starts the wait for turning [method] off (a wait already running is not restarted). Returns the pending turn-off. */
    @Synchronized
    fun start(method: SyncMethod, now: Instant): PendingSyncOff {
        _pending.value?.let { return it }

        val started = PendingSyncOff(method, now.plus(WAIT))
        prefs.edit().putString(KEY_METHOD, method.key).putLong(KEY_AT, started.at.toEpochMilli()).apply()
        _pending.value = started
        onStart?.invoke()
        return started
    }

    /** Drops the pending turn-off: the way simply stays on. */
    @Synchronized
    fun cancel() {
        prefs.edit().remove(KEY_METHOD).remove(KEY_AT).apply()
        _pending.value = null
    }

    private fun load(): PendingSyncOff? {
        val method = SyncMethod.fromKey(prefs.getString(KEY_METHOD, null)) ?: return null
        val at = prefs.getLong(KEY_AT, 0L).takeIf { it > 0 } ?: return null
        return PendingSyncOff(method, Instant.ofEpochMilli(at))
    }

    companion object {
        /** How long turning off the last way of syncing waits (the same 10 minutes as the other looser changes). */
        val WAIT: Duration = Duration.ofMinutes(10)
        private const val KEY_METHOD = "method"
        private const val KEY_AT = "at"
    }
}
