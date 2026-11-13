package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncSendLog
import kotlinx.serialization.json.Json
import java.io.File
import java.time.OffsetDateTime

/**
 * The phone's send log ([SyncSendLog]) in a private file (sync-sendlog.json: times only, device-local, in no backup, never sent). Thread-safe.
 */
class SyncSendLogStore(private val file: File) {
    private var log: SyncSendLog = load()

    /** Called after the log changed, so a shown board can refresh. */
    @Volatile
    var onChanged: () -> Unit = {}

    /** The log as it is now. */
    @get:Synchronized
    val value: SyncSendLog get() = log

    /** This phone's file written at [writtenAt] reached [method] at [reachedAt]. */
    @Synchronized
    fun record(method: String, writtenAt: OffsetDateTime, reachedAt: OffsetDateTime) {
        val next = log.record(method, writtenAt, reachedAt)
        if (next == log) return
        log = next
        save(next)
        runCatching { onChanged() }
    }

    /** Forgets everything (left the group: the files belonged to it). */
    @Synchronized
    fun clear() {
        log = SyncSendLog()
        runCatching { file.delete() }
    }

    private fun save(value: SyncSendLog) {
        runCatching {
            file.parentFile?.mkdirs()
            val temporary = File(file.parentFile, "${file.name}.tmp")
            temporary.writeText(value.toJson().toString())
            if (!temporary.renameTo(file)) {
                file.delete()
                temporary.renameTo(file)
            }
        }
    }

    private fun load(): SyncSendLog =
        if (file.exists()) runCatching { SyncSendLog.fromJson(Json.parseToJsonElement(file.readText())) }.getOrDefault(SyncSendLog()) else SyncSendLog()
}
