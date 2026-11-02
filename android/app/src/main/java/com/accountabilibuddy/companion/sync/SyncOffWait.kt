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
 * at any time. There is no "apply now": nothing shortens the wait, and turning another way on does not either.
 *
 * It also remembers since when each way has been on, unchanged ([observe]). A way only counts as a way of syncing ([counted]) after it has
 * been on and pointing at the same place for [WAIT], so turning a dummy way on (an empty folder, an address that goes nowhere) does not make
 * the real one "not the last".
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

    /**
     * Brings the "on since" times in step with [settings]: a way that is off has none; a way that is on and was not (or points elsewhere now)
     * starts at [now]. The first time this ever runs, what is on already counts (an update must not hold up an old install).
     */
    @Synchronized
    fun observe(settings: PhoneSyncSettings, now: Instant) {
        val first = !prefs.contains(KEY_SEEN)
        val editor = prefs.edit()
        for (method in SyncMethod.entries) {
            val sinceKey = "since.${method.key}"
            val pointsKey = "points.${method.key}"
            if (!method.isLive(settings)) {
                editor.remove(sinceKey).remove(pointsKey)
                continue
            }

            // Hashed, so the relay's admission key is never written here in the clear.
            val points = method.fingerprint(settings).hashCode().toString()
            if (prefs.contains(sinceKey) && prefs.getString(pointsKey, null) == points) continue

            editor.putLong(sinceKey, if (first) 0L else now.toEpochMilli()).putString(pointsKey, points)
        }
        editor.putBoolean(KEY_SEEN, true).apply()
    }

    /** Whether [method] is on in [settings] and has been, unchanged, for [WAIT] (the ones that count as a way of syncing). */
    @Synchronized
    fun counted(method: SyncMethod, settings: PhoneSyncSettings, now: Instant): Boolean {
        observe(settings, now)
        if (!method.isLive(settings)) return false

        return !now.isBefore(Instant.ofEpochMilli(prefs.getLong("since.${method.key}", Long.MAX_VALUE)).plus(WAIT))
    }

    /** The ways of [settings] that count right now. */
    fun countedWays(settings: PhoneSyncSettings, now: Instant): List<SyncMethod> = SyncMethod.entries.filter { counted(it, settings, now) }

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
        private const val KEY_SEEN = "seen"
    }
}
