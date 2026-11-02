package com.accountabilibuddy.companion.sync

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * The wait for turning off the last way of syncing (the pattern of `SleepSettingsSync` and `PlacementGuard`, a looser change): the user asks,
 * the way stays on and [pending] says when it turns off; [SyncCoordinator] applies it when the time has come and no focus session runs
 * (a wait that ends in a session applies after it). Kept in prefs "syncOff" (never synced), so a restart does not skip it. Cancelling works
 * at any time. There is no "apply now": nothing shortens the wait, and turning another way on does not either.
 *
 * It also remembers since when each way has been on, unchanged ([observe]), and whether it has ever brought a file from another device of the
 * group ([markDelivered]). A way only counts as a way of syncing ([counted]) when both hold: on and pointing at the same place for [WAIT], and
 * having delivered. So a dummy way (an empty folder, an address that goes nowhere) neither makes the real one "not the last", nor is itself held
 * back: a way that has never brought anything can be pointed elsewhere or turned off at once, because nothing depends on it.
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
     * starts at [now] and forgets what it delivered from the old place. The first time this ever runs, what is on already counts for time
     * (an update must not hold up an old install).
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

            val points = pointsOf(method, settings)
            if (prefs.contains(sinceKey) && prefs.getString(pointsKey, null) == points) continue

            // Pointed somewhere else (or newly on): what it delivered before came from the old place.
            if (prefs.getString(deliveredKey(method), null) != points) editor.remove(deliveredKey(method))
            editor.putLong(sinceKey, if (first) 0L else now.toEpochMilli()).putString(pointsKey, points)
        }
        editor.putBoolean(KEY_SEEN, true).apply()
    }

    /**
     * Whether [method] is on in [settings], has been, unchanged, for [WAIT], and has delivered a file from another device (the ones that count
     * as a way of syncing). Reads only: [observe] keeps the "on since" times current, and the callers that change settings run it first.
     */
    @Synchronized
    fun counted(method: SyncMethod, settings: PhoneSyncSettings, now: Instant): Boolean {
        if (!delivered(method, settings)) return false

        return !now.isBefore(Instant.ofEpochMilli(prefs.getLong("since.${method.key}", Long.MAX_VALUE)).plus(WAIT))
    }

    /** The ways of [settings] that count right now. */
    fun countedWays(settings: PhoneSyncSettings, now: Instant): List<SyncMethod> = SyncMethod.entries.filter { counted(it, settings, now) }

    /** Whether [method] is on, pointing where [settings] say, and has brought a file from another device of the group from there. */
    @Synchronized
    fun delivered(method: SyncMethod, settings: PhoneSyncSettings): Boolean =
        method.isLive(settings) && prefs.getString(deliveredKey(method), null) == pointsOf(method, settings)

    /** Records that a file from another device came by [method]. Ignored for a way that is not on. */
    @Synchronized
    fun markDelivered(method: SyncMethod, settings: PhoneSyncSettings) {
        if (!method.isLive(settings)) return

        prefs.edit().putString(deliveredKey(method), pointsOf(method, settings)).apply()
    }

    /**
     * Records that this phone wrote [deviceId]'s file ([writtenAt]) to [method]'s place, to carry it on, so reading it back is not a delivery:
     * otherwise an empty folder the phone carries files into would soon look like one that brings them.
     */
    @Synchronized
    fun noteCarried(method: SyncMethod, deviceId: UUID, writtenAt: Instant) {
        val key = carriedKey(method, deviceId)
        if (writtenAt.toEpochMilli() > prefs.getLong(key, 0L)) prefs.edit().putLong(key, writtenAt.toEpochMilli()).apply()
    }

    /** Whether this phone itself wrote that file (or a newer one of the same device) to [method]'s place ([noteCarried]). */
    @Synchronized
    fun wasCarried(method: SyncMethod, deviceId: UUID, writtenAt: Instant): Boolean =
        writtenAt.toEpochMilli() <= prefs.getLong(carriedKey(method, deviceId), 0L)

    /**
     * Once, on the first run of this version, for the files the phone holds ([held]; each came by its [ReceivedDevice.method]):
     * - Drive and the relay count as having delivered when they are on and a held file came by them. The shared folder is never migrated: a
     *   held file's method has no destination and survives a pointing elsewhere, so it could mark a wrongly picked folder; a real folder earns
     *   its mark from the pass that runs at start-up.
     * - Every held file is recorded as carried by each way other than the one it came by: the old version wrote those copies there itself, so
     *   reading them back must not look like a delivery (an empty folder holding carried copies would otherwise earn the mark).
     * (Before this, a way counted as soon as it had been on for 10 minutes.)
     */
    @Synchronized
    fun migrateDelivery(settings: PhoneSyncSettings, held: List<ReceivedDevice>) {
        if (prefs.contains(KEY_DELIVERY)) return

        val editor = prefs.edit()
        for (device in held) {
            val cameBy = SyncMethod.fromCardMethod(device.method)
            for (way in SyncMethod.entries) {
                if (way == cameBy) continue

                val carriedKey = carriedKey(way, device.file.device.deviceId)
                val writtenAt = device.file.device.writtenAt.toEpochMilli()
                if (writtenAt > prefs.getLong(carriedKey, 0L)) editor.putLong(carriedKey, writtenAt)
            }

            if (cameBy != null && cameBy != SyncMethod.Folder && cameBy.isLive(settings)) {
                editor.putString(deliveredKey(cameBy), pointsOf(cameBy, settings))
            }
        }
        editor.putBoolean(KEY_DELIVERY, true).apply()
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

    // What the way points at, hashed, so the relay's admission key is never written here in the clear.
    private fun pointsOf(method: SyncMethod, settings: PhoneSyncSettings): String = method.fingerprint(settings).hashCode().toString()

    private fun deliveredKey(method: SyncMethod): String = "delivered.${method.key}"

    private fun carriedKey(method: SyncMethod, deviceId: UUID): String = "out.${method.key}.$deviceId"

    companion object {
        /** How long turning off the last way of syncing waits (the same 10 minutes as the other looser changes). */
        val WAIT: Duration = Duration.ofMinutes(10)
        private const val KEY_METHOD = "method"
        private const val KEY_AT = "at"
        private const val KEY_SEEN = "seen"
        private const val KEY_DELIVERY = "deliveryMigrated"
    }
}
