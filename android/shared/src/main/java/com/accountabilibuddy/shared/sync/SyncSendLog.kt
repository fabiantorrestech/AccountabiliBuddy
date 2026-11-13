package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.OffsetDateTime

/** One put of this phone's file on a sync method: the file's own time and when it reached the method (both times only, ADR 86). */
data class SyncSendEntry(val writtenAt: OffsetDateTime, val reachedAt: OffsetDateTime)

/**
 * A small per-method log of this phone's own puts (times only, the newest [MAX_PER_METHOD] per method; the PC's `SyncSendLog`). The
 * confirmation board needs it: whether a receiver's last file could already have confirmed my newest one is decided against the receiver's OWN
 * write time ([SyncSentFacts.previousSentAt], SYNC1a Q850). Pure and immutable; the phone keeps it in a private file that is in no backup.
 */
data class SyncSendLog(val entries: Map<String, List<SyncSendEntry>> = emptyMap()) {
    /** My file written at [writtenAt] reached [method] at [reachedAt]. */
    fun record(method: String, writtenAt: OffsetDateTime, reachedAt: OffsetDateTime): SyncSendLog {
        if (method !in SyncReceiptParts.METHODS) return this
        val list = (entries[method].orEmpty() + SyncSendEntry(writtenAt, reachedAt)).takeLast(MAX_PER_METHOD)
        return SyncSendLog(entries + (method to list))
    }

    /** The newest `writtenAt` of any put; null when nothing was put yet. */
    val newest: OffsetDateTime? get() = entries.values.flatten().map { it.writtenAt }.maxWithOrNull { a, b -> DotNetTime.compare(a, b) }

    /**
     * What the sender knows for one receiver ([SyncSentFacts]): the newest file, when that file reached each method, and per method the newest
     * of my files that had reached it by [receiverWrote], the receiver's own latest write time.
     */
    fun facts(receiverWrote: OffsetDateTime?): SyncSentFacts? {
        val newest = newest ?: return null
        val reached = LinkedHashMap<String, OffsetDateTime>()
        val previous = LinkedHashMap<String, OffsetDateTime>()
        for ((method, list) in entries) {
            list.filter { DotNetTime.same(it.writtenAt, newest) }.minWithOrNull { a, b -> DotNetTime.compare(a.reachedAt, b.reachedAt) }?.let { reached[method] = it.reachedAt }
            if (receiverWrote != null) {
                list.filter { DotNetTime.compare(it.reachedAt, receiverWrote) <= 0 }.maxWithOrNull { a, b -> DotNetTime.compare(a.writtenAt, b.writtenAt) }
                    ?.let { previous[method] = it.writtenAt }
            }
        }

        return SyncSentFacts(newest, reached, previous.ifEmpty { null })
    }

    fun toJson(): JsonObject = buildJsonObject {
        for ((method, list) in entries) {
            put(method, JsonArray(list.map { buildJsonObject { put("w", it.writtenAt.toString()); put("r", it.reachedAt.toString()) } }))
        }
    }

    companion object {
        const val MAX_PER_METHOD = 24

        /** Reads what [toJson] wrote; an entry that does not read is dropped, never failing the log. */
        fun fromJson(json: JsonElement?): SyncSendLog {
            val root = json as? JsonObject ?: return SyncSendLog()
            val map = LinkedHashMap<String, List<SyncSendEntry>>()
            for ((method, value) in root) {
                if (method !in SyncReceiptParts.METHODS) continue
                val list = (value as? JsonArray).orEmpty().mapNotNull { element ->
                    runCatching {
                        val item = element as JsonObject
                        SyncSendEntry(
                            OffsetDateTime.parse((item["w"] as kotlinx.serialization.json.JsonPrimitive).content),
                            OffsetDateTime.parse((item["r"] as kotlinx.serialization.json.JsonPrimitive).content),
                        )
                    }.getOrNull()
                }.takeLast(MAX_PER_METHOD)
                if (list.isNotEmpty()) map[method] = list
            }

            return SyncSendLog(map)
        }
    }
}
