package com.accountabilibuddy.watch.link

import android.content.Context
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.accountabilibuddy.shared.WatchText
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import java.io.File
import java.time.Instant

/**
 * The last snapshot from the phone (ADR 21), kept as a file in the app's private folder so the watch shows it after a
 * restart, and as a flow for the screens. Cleared when the phone says the watch isn't linked. Each change redraws the
 * Tile and the complication.
 */
object SnapshotStore {
    private const val FILE = "snapshot.json"

    private val lock = Any()
    private val _snapshot = MutableStateFlow<WatchSnapshot?>(null)
    private val background = SerialBackground("snapshot-store")
    @Volatile private var loaded = false

    fun snapshot(context: Context): StateFlow<WatchSnapshot?> {
        load(context)
        return _snapshot.asStateFlow()
    }

    /**
     * Keeps [snapshot]: the flow changes at once; the file, the Tile and complication and the alarms follow on a
     * background thread, and only when something changed (the phone resends the same data every few minutes).
     * [wait]: also wait for those (for the Data Layer listener, which is off the main thread and may be stopped
     * right after).
     */
    fun save(context: Context, snapshot: WatchSnapshot, wait: Boolean = false) {
        val app = context.applicationContext
        val now = Instant.now()
        val change = synchronized(lock) {
            val previous = if (loaded) _snapshot.value else null
            loaded = true
            _snapshot.value = snapshot
            when {
                previous == null || !sameContent(previous, snapshot) -> Change.Content
                shownAges(previous, now) != shownAges(snapshot, now) -> Change.Age
                else -> Change.None
            }
        }
        if (change == Change.None) {
            return
        }

        val job = { persist(app, snapshot, change) }
        if (wait) background.runAndWait(job) else background.run(job)
    }

    fun clear(context: Context) {
        val app = context.applicationContext
        synchronized(lock) {
            loaded = true
            _snapshot.value = null
        }
        background.runAndWait {
            File(app.filesDir, FILE).delete()
            Surfaces.refresh(app)
        }
    }

    // Runs on the background thread, in the order the saves came, so the last save is the one left in the file.
    private fun persist(context: Context, snapshot: WatchSnapshot, change: Change) {
        val file = File(context.filesDir, FILE)
        val temporary = File(context.filesDir, "$FILE.tmp")
        temporary.writeText(WatchSnapshotJson.write(snapshot).toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }

        Surfaces.refresh(context)
        if (change == Change.Content) {
            com.accountabilibuddy.watch.alerts.PhaseAlertScheduler.schedule(context, snapshot)
        }
    }

    // Everything the screens, Tile and alarms use, apart from the timestamps (they only move the "updated … ago" line).
    private fun sameContent(old: WatchSnapshot, new: WatchSnapshot): Boolean = withoutTimes(old) == withoutTimes(new)

    private fun withoutTimes(snapshot: WatchSnapshot): WatchSnapshot = snapshot.copy(
        createdAt = Instant.EPOCH,
        dataAt = Instant.EPOCH,
        sources = snapshot.sources.map { it.copy(dataAt = Instant.EPOCH) },
    )

    // The "updated … ago" texts the watch shows for the snapshot (the glance's and the "where from" page's).
    private fun shownAges(snapshot: WatchSnapshot, now: Instant): List<String?> =
        listOf(WatchText.updatedAgo(snapshot.dataAt, now)) + snapshot.sources.map { WatchText.updatedAgo(it.dataAt, now) }

    private enum class Change { None, Age, Content }

    private fun load(context: Context) = synchronized(lock) {
        if (loaded) {
            return@synchronized
        }

        // ADR 45: the watch's own PC link is gone; its stored pairing is deleted once.
        PcLeftovers.deleteOnce(context)
        val file = File(context.filesDir, FILE)
        _snapshot.value = if (file.exists()) {
            runCatching { Json.parseToJsonElement(file.readText()) as? JsonObject }.getOrNull()?.let(WatchSnapshotJson::read)
        } else {
            null
        }
        loaded = true
    }
}
