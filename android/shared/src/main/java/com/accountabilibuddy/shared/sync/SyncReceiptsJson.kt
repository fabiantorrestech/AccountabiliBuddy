package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.OffsetDateTime
import java.util.UUID

/**
 * A sync file's `receipts` (ADR 86) as JSON, by hand (R8-safe, no reflection), in the PC's shape: the names of [SyncReceipt] in field
 * order, an absent value left out, times as .NET writes them. Read leniently like the PC's `SyncLenientListConverter`: a value that is not a
 * list reads as none, an entry that is not an object or has a value of the wrong type (a time that is not a time, a map that is not an
 * object) is dropped, never failing the file. A missing id reads as the empty id and a missing time as year 1, as the PC reads them; the clean-up
 * ([SyncReceiptParts.clean]) drops what is left of no use.
 */
object SyncReceiptsJson {
    fun read(value: JsonElement?): List<SyncReceipt>? {
        val list = value as? JsonArray ?: return null
        return list.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching { readEntry(item) }.getOrNull()
        }
    }

    fun write(items: List<SyncReceipt>): JsonArray = JsonArray(items.map { item ->
        buildJsonObject {
            put("from", item.from.toString())
            put("held", GoalJson.timeValue(item.held))
            item.byMethod?.let { map -> put("byMethod", buildJsonObject { map.forEach { (key, time) -> put(key, GoalJson.timeValue(time)) } }) }
            item.failed?.let { map -> put("failed", buildJsonObject { map.forEach { (key, reason) -> put(key, reason) } }) }
            item.parts?.let { map ->
                put("parts", buildJsonObject {
                    map.forEach { (key, part) ->
                        put(key, buildJsonObject {
                            put("at", GoalJson.timeValue(part.at))
                            put("state", part.state)
                        })
                    }
                })
            }
            item.history?.let { months ->
                put("history", JsonArray(months.map { month ->
                    buildJsonObject {
                        put("month", month.month)
                        put("version", GoalJson.timeValue(month.version))
                    }
                }))
            }
            item.failedAt?.let { map -> put("failedAt", buildJsonObject { map.forEach { (key, time) -> put(key, GoalJson.timeValue(time)) } }) }
        }
    })

    private fun readEntry(item: JsonObject): SyncReceipt {
        val from = field(item, "from")?.let { uuid(it) } ?: UUID(0L, 0L)
        val held = field(item, "held")?.let { time(it) } ?: DotNetTime.MIN
        val byMethod = field(item, "byMethod").orNull()?.let { map(it) { value -> time(value) } }
        // A null value in a map is allowed (the PC's clean-up drops it); any other value of the wrong type drops the entry.
        val failed = field(item, "failed").orNull()?.let { source ->
            map(source) { value -> if (value is JsonNull) null else text(value) }.filterValues { it != null }.mapValues { it.value!! }
        }
        val parts = field(item, "parts").orNull()?.let { source ->
            map(source) { value -> if (value is JsonNull) null else part(value) }.filterValues { it != null }.mapValues { it.value!! }
        }
        val history = field(item, "history").orNull()?.let { months(it) }
        val failedAt = field(item, "failedAt").orNull()?.let { map(it) { value -> time(value) } }
        return SyncReceipt(from, held, byMethod, failed, parts, history, failedAt)
    }

    // The PC reads property names without regard to case (its Web defaults); so do the receipts here. (The rest of the phone's reader reads exact names, which is what both apps write.)
    private fun field(item: JsonObject, name: String): JsonElement? = item[name] ?: item.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value

    private fun JsonElement?.orNull(): JsonElement? = if (this == null || this is JsonNull) null else this

    private fun <T> map(value: JsonElement, read: (JsonElement) -> T): Map<String, T> {
        val obj = value as? JsonObject ?: error("not an object")
        val result = LinkedHashMap<String, T>()
        obj.forEach { (key, element) -> result[key] = read(element) }
        return result
    }

    private fun months(value: JsonElement): List<SyncHistoryReceipt> {
        val array = value as? JsonArray ?: error("not a list")
        return array.mapNotNull { element ->
            if (element is JsonNull) return@mapNotNull null
            val obj = element as? JsonObject ?: error("not an object")
            val month = field(obj, "month").orNull()?.let { text(it) }
            val version = field(obj, "version")?.let { time(it) } ?: DotNetTime.MIN
            // A month this app cannot use (none, or not "yyyy-MM") is cleaned away with the rest of the entry's lists.
            month?.let { SyncHistoryReceipt(it, version) }
        }
    }

    // The state may be missing or null (the clean-up drops such a part); a time of the wrong type fails the entry.
    private fun part(value: JsonElement): SyncPartReceipt? {
        val obj = value as? JsonObject ?: error("not an object")
        val at = field(obj, "at")?.let { time(it) } ?: DotNetTime.MIN
        val state = field(obj, "state").orNull()?.let { text(it) } ?: return null
        return SyncPartReceipt(at, state)
    }

    private fun text(value: JsonElement): String {
        val primitive = value as? JsonPrimitive ?: error("not a text")
        if (!primitive.isString) error("not a text")
        return primitive.content
    }

    private fun uuid(value: JsonElement): UUID {
        val content = text(value)
        if (content.length != 36) error("not an id")
        return UUID.fromString(content)
    }

    private fun time(value: JsonElement): OffsetDateTime = GoalJson.parseTime(text(value))
}
