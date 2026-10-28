package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.SettingsJson
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDocument
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * The sleep settings both ways (ADR 35): the phone's goes out in its sync file with the time the user last changed it
 * here, and a PC's "sleep" document that's newer is taken here (from any sync file). The newest
 * edit wins on every device. The edit time is kept in prefs "sleepSync".
 *
 * Hard rules, as on the PC (`SleepSettingsService`): turning sleep mode on applies at once and cancels a pending off;
 * turning it off is a looser change, so it is refused while a PC focus session runs (the check the placement switch
 * uses) and otherwise waits [OFF_WAIT] with sleep mode still on. The time is kept in prefs ("pendingOffAt"), so a
 * restart doesn't skip it, and the off applies only while no session runs. An applied off goes out with
 * [SleepSettings.offWaited], so a PC taking it from the sync file applies it at once; an off received from a PC that
 * didn't wait (an older version) waits here too.
 */
class SleepSettingsSync(context: Context, private val store: CompanionStore) {
    private val app = context.applicationContext as CompanionApp
    private val prefs = context.getSharedPreferences("sleepSync", Context.MODE_PRIVATE)

    /** Called when a wait for turning off starts, so the app can apply it when it is due. */
    var onPendingOff: (() -> Unit)? = null

    /** When the sleep settings last changed here (by the user, or taken from a newer PC); null before the first. */
    val editedAt: Instant?
        get() = prefs.getString(KEY_EDITED, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }

    /** When a pending off is due; null when none is waiting. */
    @Synchronized
    fun pendingOffAt(): Instant? {
        val at = prefs.getLong(KEY_OFF_AT, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli) ?: return null
        if (!store.state.value.sleep.enabled) {
            // Already off: the wait is no longer needed.
            prefs.edit().remove(KEY_OFF_AT).apply()
            return null
        }

        return at
    }

    /** "Sleep mode turns off at 4:05 PM", while an off is pending; otherwise null. */
    fun pendingOffText(): String? =
        pendingOffAt()?.let { "Sleep mode turns off at ${CLOCK.format(it.atZone(ZoneId.systemDefault()))}" }

    /** True while a PC focus session runs: sleep mode can't be turned off then. */
    fun locked(now: Instant = Instant.now()): Boolean = PlacementSession.current(app, now) != null

    /**
     * The user changed the sleep settings on this phone: save them and stamp the edit. Turning sleep mode off is held
     * instead (see the class comment): everything else is saved, sleep mode stays on, and nothing is stamped for the
     * request itself. Saving with sleep mode on leaves a pending off alone; turning it back on is [cancelPendingOff].
     * @return null when everything was saved as asked; otherwise what happened to the off.
     */
    @Synchronized
    fun saveEdit(settings: SleepSettings, now: Instant = Instant.now()): String? {
        val current = store.state.value.sleep
        var toSave = settings
        var note: String? = null
        if (current.enabled && !settings.enabled) {
            if (locked(now)) {
                note = "Sleep mode stays on: turning it off is locked while a focus session is running."
            } else {
                startPendingOff(now)
                note = pendingOffText()
            }

            toSave = settings.copy(enabled = true, offWaited = false)
        } else if (settings.enabled) {
            toSave = settings.copy(offWaited = false)
        }

        // Asking for the off changes no shared field, so it isn't stamped (the apply is).
        if (toSave != current) {
            store.saveSleep(toSave)
            prefs.edit().putString(KEY_EDITED, now.toString()).apply()
        }

        return note
    }

    /** Drops a pending off, which is turning sleep mode back on: it stays on, so nothing changes or goes out. */
    @Synchronized
    fun cancelPendingOff() {
        prefs.edit().remove(KEY_OFF_AT).apply()
    }

    /**
     * Turns sleep mode off once its wait is over and no session is running (a wait that ends in a session applies after
     * it, as on the PC). The off is marked as waited. True when it was applied.
     */
    @Synchronized
    fun applyPendingOffIfDue(now: Instant = Instant.now()): Boolean {
        val at = pendingOffAt() ?: return false
        if (now.isBefore(at) || locked(now)) {
            return false
        }

        prefs.edit().remove(KEY_OFF_AT).apply()
        store.saveSleep(store.state.value.sleep.copy(enabled = false, offWaited = true))
        prefs.edit().putString(KEY_EDITED, now.toString()).apply()
        return true
    }

    /** This phone's sleep settings as a document for its file; null until they were changed here or taken. */
    fun document(): SyncDocument? {
        val at = editedAt ?: return null
        return SyncDocument(SyncDocument.SLEEP_KEY, SettingsJson.sleepJson(store.state.value.sleep).toString(), at)
    }

    /**
     * Takes the newest PC "sleep" document newer than this phone's edit. True when something was taken. A received on
     * cancels a pending off; a received off applies at once only when that PC already waited for it
     * ([SleepSettings.offWaited]), otherwise it waits here too (also during a session; it then applies after it).
     */
    @Synchronized
    fun applyFrom(devices: List<ReceivedDevice>): Boolean {
        val newest = devices
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC }
            .mapNotNull { device -> device.file.documents.firstOrNull { it.key == SyncDocument.SLEEP_KEY } }
            .maxByOrNull { it.editedAt } ?: return false
        val mine = editedAt
        if (mine != null && !newest.editedAt.isAfter(mine)) return false

        val received = runCatching { SettingsJson.sleep(Json.parseToJsonElement(newest.json).jsonObject) }.getOrNull() ?: return false
        if (received.enabled || !store.state.value.sleep.enabled || received.offWaited) {
            prefs.edit().remove(KEY_OFF_AT).apply()
            store.saveSleep(received)
        } else {
            startPendingOff(Instant.now())
            store.saveSleep(received.copy(enabled = true, offWaited = false))
        }

        prefs.edit().putString(KEY_EDITED, newest.editedAt.toString()).apply()
        return true
    }

    // A running wait is not restarted by asking again.
    private fun startPendingOff(now: Instant) {
        if (prefs.getLong(KEY_OFF_AT, 0L) == 0L) {
            prefs.edit().putLong(KEY_OFF_AT, now.plus(OFF_WAIT).toEpochMilli()).apply()
        }

        onPendingOff?.invoke()
    }

    private companion object {
        val OFF_WAIT: Duration = Duration.ofMinutes(10)
        val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a")
        const val KEY_EDITED = "editedAt"
        const val KEY_OFF_AT = "pendingOffAt"
    }
}
