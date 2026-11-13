package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncReceipt
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncReceiptsJson
import com.accountabilibuddy.shared.sync.SyncReceiptsLocal
import kotlinx.serialization.json.Json
import java.io.File
import java.time.OffsetDateTime
import java.util.UUID

/**
 * The receiver-local record behind this phone's receipts (ADR 86, [SyncReceiptsLocal]; the PC's `SyncReceiptsStore`): per sender, by which
 * method its newest file arrived, why a read failed, what each part of its last file came to, which goal history months were merged. Kept in a
 * private file (sync-receipts.json: device-local, in no backup, never sent as it is; the sync file carries only [forFile]). Recording never
 * writes the phone's own file: receiving a file must not trigger a write, so receipts leave with the next scheduled write. Ids, times and codes
 * only. Thread-safe.
 */
class SyncReceiptsStore(private val file: File) {
    private var local: SyncReceiptsLocal = load()

    /** Called (on the caller's thread) after the record changed, so a shown board can refresh. */
    @Volatile
    var onChanged: () -> Unit = {}

    /** The record as it is now. */
    @get:Synchronized
    val value: SyncReceiptsLocal get() = local

    /** @see SyncReceiptsLocal.recordArrival */
    fun recordArrival(from: UUID, method: String, writtenAt: OffsetDateTime) = update { it.recordArrival(from, method, writtenAt) }

    /** @see SyncReceiptsLocal.recordFailure */
    fun recordFailure(from: UUID, method: String, reason: String, at: OffsetDateTime) = update { it.recordFailure(from, method, reason, at) }

    /** @see SyncReceiptsLocal.recordParts */
    fun recordParts(from: UUID, at: OffsetDateTime, states: Map<String, String>) = update { it.recordParts(from, at, states) }

    /** @see SyncReceiptsLocal.recordHistory */
    fun recordHistory(from: UUID, month: String, version: OffsetDateTime) = update { it.recordHistory(from, month, version) }

    /** Forgets the rows of devices that are gone (removed, ADR 54), and the whole record when [all] (left the group). */
    fun forget(ids: Set<UUID>, all: Boolean = false) = update { if (all) SyncReceiptsLocal() else it.forget(ids) }

    /** The list this phone writes into its file: null when it has no other active device (works alone), an empty list when it holds nothing yet. */
    @Synchronized
    fun forFile(self: UUID, active: Set<UUID>): List<SyncReceipt>? = local.forFile(self, active)

    private fun update(change: (SyncReceiptsLocal) -> SyncReceiptsLocal) {
        val changed = synchronized(this) {
            val next = change(local)
            if (next == local) {
                false
            } else {
                local = next
                save(next)
                true
            }
        }

        if (changed) runCatching { onChanged() }
    }

    private fun save(record: SyncReceiptsLocal) {
        runCatching {
            file.parentFile?.mkdirs()
            val temporary = File(file.parentFile, "${file.name}.tmp")
            temporary.writeText(SyncReceiptsJson.write(record.senders).toString())
            if (!temporary.renameTo(file)) {
                file.delete()
                temporary.renameTo(file)
            }
        }
    }

    private fun load(): SyncReceiptsLocal {
        if (!file.exists()) return SyncReceiptsLocal()
        val rows = runCatching { SyncReceiptsJson.read(Json.parseToJsonElement(file.readText())) }.getOrNull()
        return SyncReceiptsLocal(SyncReceiptParts.clean(rows) ?: emptyList())
    }
}
