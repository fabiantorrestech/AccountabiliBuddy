package com.accountabilibuddy.watch.link

import android.content.Context
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import java.io.File

/**
 * The last snapshot from the phone (ADR 21), kept as a file in the app's private folder so the watch shows it after a
 * restart, and as a flow for the screens. Cleared when the phone says the watch isn't linked. Each change redraws the
 * Tile and the complication.
 */
object SnapshotStore {
    private const val FILE = "snapshot.json"

    private val lock = Any()
    private val _snapshot = MutableStateFlow<WatchSnapshot?>(null)
    @Volatile private var loaded = false

    fun snapshot(context: Context): StateFlow<WatchSnapshot?> {
        load(context)
        return _snapshot.asStateFlow()
    }

    fun save(context: Context, snapshot: WatchSnapshot) = synchronized(lock) {
        val file = File(context.filesDir, FILE)
        val temporary = File(context.filesDir, "$FILE.tmp")
        temporary.writeText(WatchSnapshotJson.write(snapshot).toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }

        loaded = true
        _snapshot.value = snapshot
        Surfaces.refresh(context)
        com.accountabilibuddy.watch.alerts.PhaseAlertScheduler.schedule(context, snapshot)
    }

    fun clear(context: Context) = synchronized(lock) {
        File(context.filesDir, FILE).delete()
        loaded = true
        _snapshot.value = null
        Surfaces.refresh(context)
    }

    private fun load(context: Context) = synchronized(lock) {
        if (loaded) {
            return@synchronized
        }

        val file = File(context.filesDir, FILE)
        _snapshot.value = if (file.exists()) {
            runCatching { Json.parseToJsonElement(file.readText()) as? JsonObject }.getOrNull()?.let(WatchSnapshotJson::read)
        } else {
            null
        }
        loaded = true
    }
}
