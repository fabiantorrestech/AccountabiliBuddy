package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.SettingsJson
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDocument
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import java.time.Instant

/**
 * The sleep settings both ways (ADR 35): the phone's goes out in its sync file with the time the user last changed it
 * here, and a PC's "sleep" document that's newer is taken here (from any sync file, not only the link). The newest
 * edit wins on every device. The edit time is kept in prefs "sleepSync".
 */
class SleepSettingsSync(context: Context, private val store: CompanionStore) {
    private val prefs = context.getSharedPreferences("sleepSync", Context.MODE_PRIVATE)

    /** When the sleep settings last changed here (by the user, or taken from a newer PC); null before the first. */
    val editedAt: Instant?
        get() = prefs.getString(KEY_EDITED, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }

    /** The user changed the sleep settings on this phone: save them and stamp the edit. */
    fun saveEdit(settings: SleepSettings, now: Instant = Instant.now()) {
        store.saveSleep(settings)
        prefs.edit().putString(KEY_EDITED, now.toString()).apply()
    }

    /** This phone's sleep settings as a document for its file; null until they were changed here or taken. */
    fun document(): SyncDocument? {
        val at = editedAt ?: return null
        return SyncDocument(SyncDocument.SLEEP_KEY, SettingsJson.sleepJson(store.state.value.sleep).toString(), at)
    }

    /** Takes the newest PC "sleep" document newer than this phone's edit. True when something was taken. */
    fun applyFrom(devices: List<ReceivedDevice>): Boolean {
        val newest = devices
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC }
            .mapNotNull { device -> device.file.documents.firstOrNull { it.key == SyncDocument.SLEEP_KEY } }
            .maxByOrNull { it.editedAt } ?: return false
        val mine = editedAt
        if (mine != null && !newest.editedAt.isAfter(mine)) return false

        val received = runCatching { SettingsJson.sleep(Json.parseToJsonElement(newest.json).jsonObject) }.getOrNull() ?: return false
        store.saveSleep(received)
        prefs.edit().putString(KEY_EDITED, newest.editedAt.toString()).apply()
        return true
    }

    private companion object {
        const val KEY_EDITED = "editedAt"
    }
}
