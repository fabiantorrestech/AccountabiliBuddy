package com.accountabilibuddy.shared.sync

import java.time.OffsetDateTime
import java.util.UUID

/**
 * The receiver-local record behind a device's receipts (ADR 86; the PC's `SyncReceiptsLocal`, a document `sync.receipts.local` there, a
 * private pref here; never sent as it is and never in a backup): one [SyncReceipt] row per sender this device has read a file from. Pure and
 * immutable: every operation returns a new record, and every map it touches is rebuilt so its order is the order of first insertion (the
 * PC writes the same JSON). The file's `receipts` list is [forFile] of it.
 */
data class SyncReceiptsLocal(val senders: List<SyncReceipt> = emptyList()) {
    /**
     * A file of [from] written at [writtenAt] arrived by [method] (recorded before any "already has newer data" decision, so a copy that came
     * late still proves the method works): the method's newest time and the held time move forward only, and the method's earlier failure is
     * cleared. An unknown method changes nothing.
     */
    fun recordArrival(from: UUID, method: String, writtenAt: OffsetDateTime): SyncReceiptsLocal {
        if (method !in SyncReceiptParts.METHODS || from == EMPTY_ID) return this
        return change(from) { row ->
            val previous = row.byMethod?.get(method) ?: DotNetTime.MIN
            row.copy(
                held = if (DotNetTime.compare(writtenAt, row.held) > 0) writtenAt else row.held,
                byMethod = with(row.byMethod, method, if (DotNetTime.compare(writtenAt, previous) > 0) writtenAt else previous),
                failed = without(row.failed, method),
                failedAt = without(row.failedAt, method),
            )
        }
    }

    /**
     * The last attempt to read [from]'s file by [method] failed for [reason] (a code of [SyncReceiptParts.FAILURES]), at [at] by this device's
     * clock (null: unknown; the board then treats it as current).
     */
    fun recordFailure(from: UUID, method: String, reason: String, at: OffsetDateTime? = null): SyncReceiptsLocal {
        if (method !in SyncReceiptParts.METHODS || reason !in SyncReceiptParts.FAILURES || from == EMPTY_ID) return this
        return change(from) { row ->
            row.copy(failed = with(row.failed, method, reason), failedAt = if (at != null) with(row.failedAt, method, at) else without(row.failedAt, method))
        }
    }

    /** The outcome of the last accept of [from]'s file written at [at], per part code. Unknown codes and states are ignored. */
    fun recordParts(from: UUID, at: OffsetDateTime, states: Map<String, String>): SyncReceiptsLocal {
        if (from == EMPTY_ID) return this
        return change(from) { row ->
            var parts = row.parts
            for ((code, state) in states) {
                if (code in SyncReceiptParts.CODES && state in SyncReceiptParts.STATES) parts = with(parts, code, SyncPartReceipt(at, state))
            }

            row.copy(parts = parts)
        }
    }

    /** A goal history month file of [from] was merged ("yyyy-MM", the version's written time); the newest [SyncReceiptParts.MAX_MONTHS] stay. */
    fun recordHistory(from: UUID, month: String, version: OffsetDateTime): SyncReceiptsLocal {
        if (from == EMPTY_ID || !SyncReceiptParts.isMonth(month)) return this
        return change(from) { row ->
            val months = (row.history.orEmpty().filter { it.month != month } + SyncHistoryReceipt(month, version))
                .sortedByDescending { it.month }
                .take(SyncReceiptParts.MAX_MONTHS)
            row.copy(history = months)
        }
    }

    /** Forgets the rows of devices that are gone (removed, ADR 54). */
    fun forget(ids: Set<UUID>): SyncReceiptsLocal = if (ids.isEmpty()) this else SyncReceiptsLocal(senders.filter { it.from !in ids })

    /**
     * The list this device writes into its file: a row for each of [active] except [self], never a removed device, cleaned and bounded
     * ([SyncReceiptParts.clean]); null only when there is no other active device (works alone: nothing is written); an empty list when there
     * are other devices but nothing is held from them yet (so a receiver can tell "writes receipts, none yet" from an older app).
     */
    fun forFile(self: UUID, active: Set<UUID>): List<SyncReceipt>? {
        if (active.none { it != self }) return null
        return SyncReceiptParts.clean(senders.filter { it.from != self && it.from in active })
    }

    private fun change(from: UUID, change: (SyncReceipt) -> SyncReceipt): SyncReceiptsLocal {
        val index = senders.indexOfFirst { it.from == from }
        val rows = senders.toMutableList()
        if (index < 0) rows.add(change(SyncReceipt(from, DotNetTime.MIN))) else rows[index] = change(rows[index])
        return SyncReceiptsLocal(rows)
    }

    private fun <T> with(items: Map<String, T>?, key: String, value: T): Map<String, T> {
        val copy = LinkedHashMap<String, T>()
        var placed = false
        items?.forEach { (name, existing) ->
            val same = name == key
            copy[name] = if (same) value else existing
            placed = placed || same
        }

        if (!placed) copy[key] = value
        return copy
    }

    private fun <T> without(items: Map<String, T>?, key: String): Map<String, T>? =
        items?.filterKeys { it != key }?.takeIf { it.isNotEmpty() }

    companion object {
        private val EMPTY_ID = UUID(0L, 0L)
    }
}
