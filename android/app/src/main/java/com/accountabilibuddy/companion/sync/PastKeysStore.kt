package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncPastKey
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.time.Duration
import java.time.Instant
import java.util.Base64

/**
 * The group's past keys (ADR 56): on every key switch the outgoing group id and AES key are added to a local list, so a removed
 * device's backup, sealed with the key of its day, can still be opened after later removals. The list holds the derived AES key,
 * never the link code, and is kept in sync-past-keys.json as one string sealed by [TokenVault]. It keeps the 20 newest keys, none
 * older than a year. Devices share it in their sealed sync files ([SyncPastKey], merged as a set by group id through [merge]), so
 * one that joins later learns it too. A past key is used for one thing: opening a backup whose header names its group
 * ([keyFor]); never to read or write a sync file.
 */
class PastKeysStore(private val file: File, private val now: () -> Instant = Instant::now) {
    private var keys: List<SyncPastKey> = emptyList()

    init {
        load()
    }

    /** The keys to write into this phone's sealed sync file; null when there are none. */
    @Synchronized
    fun forFile(): List<SyncPastKey>? {
        keys = prune(keys)
        return keys.takeIf { it.isNotEmpty() }
    }

    /** The key that opens files of the group [groupId] (8 lower-case hex characters), for a backup only; null when none is kept. */
    @Synchronized
    fun keyFor(groupId: String): SyncKey? =
        keys.firstOrNull { it.groupId == groupId }?.let { SyncKey.forOpeningOnly(Base64.getDecoder().decode(it.key), it.groupId) }

    /**
     * Forgets every past key: "Leave group" (ADR 56). "Continue unsynced" and joining again keep them; a start from scratch clears the
     * app's files, this one with them.
     */
    @Synchronized
    fun clear() {
        keys = emptyList()
        file.delete()
    }

    /** The group left [outgoing] (a rekey applied or made): keeps it. */
    @Synchronized
    fun noteSwitched(outgoing: SyncKey) {
        val entry = SyncPastKey(outgoing.groupId, Base64.getEncoder().encodeToString(outgoing.key), now())
        keys = prune(keys.filter { it.groupId != entry.groupId } + entry)
        save()
    }

    /**
     * Takes in the keys another device wrote in a sealed file that opened with the group's current key: a set union by group id (a
     * key held already wins), checked for shape, capped and pruned like the local ones.
     */
    @Synchronized
    fun merge(received: List<SyncPastKey>?) {
        if (received == null) {
            return
        }

        val incoming = valid(received.take(SyncPastKey.MAX_KEPT * 2)).filter { key -> keys.none { it.groupId == key.groupId } }
        if (incoming.isEmpty()) {
            return
        }

        val merged = prune(keys + incoming)
        if (merged.map { it.groupId }.toSet() != keys.map { it.groupId }.toSet()) {
            keys = merged
            save()
        }
    }

    // Shape only: a group id of 8 hex characters and a 32-byte key; a time in the future counts as now.
    private fun valid(list: List<SyncPastKey>): List<SyncPastKey> {
        val current = now()
        return list.mapNotNull { key ->
            val id = key.groupId.lowercase()
            val bytes = runCatching { Base64.getDecoder().decode(key.key) }.getOrNull()
            if (id.length != 8 || !id.all { it in "0123456789abcdef" } || bytes == null || bytes.size != 32) {
                null
            } else {
                SyncPastKey(id, key.key, if (key.retiredAt.isAfter(current)) current else key.retiredAt)
            }
        }
    }

    // None older than a year, one per group id, the 20 newest.
    private fun prune(list: List<SyncPastKey>): List<SyncPastKey> {
        val oldest = now().minus(Duration.ofDays(SyncPastKey.KEPT_FOR_DAYS))
        return list
            .filter { !it.retiredAt.isBefore(oldest) }
            .groupBy { it.groupId }
            .map { (_, group) -> group.maxBy { it.retiredAt } }
            .sortedByDescending { it.retiredAt }
            .take(SyncPastKey.MAX_KEPT)
    }

    private fun load() {
        val sealed = runCatching { (Json.parseToJsonElement(file.readText()) as JsonObject).string("sealed") }.getOrNull() ?: return
        val text = TokenVault.open(sealed, TokenVault.PAST_KEYS_ALIAS) ?: return
        keys = prune(valid(runCatching {
            (Json.parseToJsonElement(text) as JsonArray).map { element ->
                val item = element as JsonObject
                SyncPastKey(item.string("groupId")!!, item.string("key")!!, Instant.parse(item.string("retiredAt")!!))
            }
        }.getOrDefault(emptyList())))
    }

    private fun save() {
        val list = buildJsonArray {
            keys.forEach { key ->
                add(buildJsonObject {
                    put("groupId", key.groupId)
                    put("key", key.key)
                    put("retiredAt", key.retiredAt.toString())
                })
            }
        }
        val json = buildJsonObject { put("sealed", TokenVault.seal(list.toString(), TokenVault.PAST_KEYS_ALIAS)) }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }
}
