package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * The JSON of a goal history month or year file (the PC's `GoalHistoryMonth` / `GoalHistoryYear` through `SyncJson.Options`, before the
 * sealing the transport adds): the same names in the same order, the entries as compact rows. A file this version cannot read, or whose
 * format is newer, reads as null (it is then left alone).
 */
object GoalHistoryJson {
    private val ZERO = UUID(0L, 0L)
    private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    fun writeMonth(month: GoalHistoryMonth): JsonObject = buildJsonObject {
        put("formatVersion", month.formatVersion)
        put("deviceId", month.deviceId.toString())
        put("year", month.year)
        put("month", month.month)
        put("writtenAt", GoalJson.time(month.writtenAt))
        put("entries", GoalSyncJson.entriesJson(month.entries))
    }

    fun writeYear(year: GoalHistoryYear): JsonObject = buildJsonObject {
        put("formatVersion", year.formatVersion)
        put("year", year.year)
        put("rolledBy", year.rolledBy.toString())
        put("rolledAt", GoalJson.time(year.rolledAt))
        put("months", JsonArray(year.months.map { piece ->
            buildJsonObject {
                put("deviceId", piece.deviceId.toString())
                put("month", piece.month)
                put("writtenAt", GoalJson.time(piece.writtenAt))
            }
        }))
        put("entries", GoalSyncJson.entriesJson(year.entries))
    }

    fun readMonth(json: JsonElement): GoalHistoryMonth? = try {
        val obj = json as? JsonObject
        val entries = obj?.get("entries")?.let { GoalSyncJson.readEntriesCleaned(it) }
        if (obj == null || entries == null) {
            null
        } else {
            val month = GoalHistoryMonth(
                int(obj, "formatVersion"), guid(obj, "deviceId"), int(obj, "year"), int(obj, "month"), time(obj, "writtenAt"), entries,
            )
            if (month.formatVersion in 1..GoalHistoryMonth.CURRENT_FORMAT_VERSION && month.month in 1..12 && month.deviceId != ZERO) month else null
        }
    } catch (e: IllegalArgumentException) {
        null
    }

    fun readYear(json: JsonElement): GoalHistoryYear? = try {
        val obj = json as? JsonObject
        val entries = obj?.get("entries")?.let { GoalSyncJson.readEntriesCleaned(it) }
        val pieces = (obj?.get("months") as? JsonArray)?.map { piece ->
            val item = piece as? JsonObject ?: throw IllegalArgumentException("piece")
            GoalHistoryPiece(guid(item, "deviceId"), int(item, "month"), time(item, "writtenAt"))
        }
        if (obj == null || entries == null || pieces == null) {
            null
        } else {
            val year = GoalHistoryYear(int(obj, "formatVersion"), int(obj, "year"), guid(obj, "rolledBy"), time(obj, "rolledAt"), pieces, entries)
            if (year.formatVersion in 1..GoalHistoryYear.CURRENT_FORMAT_VERSION) {
                year.copy(months = year.months.filter { it.deviceId != ZERO && it.month in 1..12 })
            } else {
                null
            }
        }
    } catch (e: IllegalArgumentException) {
        null
    }

    // A missing field is the default (0, the empty id, 0001-01-01); a field of the wrong kind is damage.
    private fun int(obj: JsonObject, name: String): Int {
        val value = obj[name] ?: return 0
        val primitive = value as? JsonPrimitive
        return primitive?.takeIf { !it.isString && it !is JsonNull }?.content?.toIntOrNull() ?: throw IllegalArgumentException(name)
    }

    private fun guid(obj: JsonObject, name: String): UUID {
        val value = obj[name] ?: return ZERO
        val text = (value as? JsonPrimitive)?.takeIf { it.isString }?.content ?: throw IllegalArgumentException(name)
        if (!guidForm.matches(text)) throw IllegalArgumentException(name)
        return UUID.fromString(text)
    }

    private fun time(obj: JsonObject, name: String): OffsetDateTime {
        val value = obj[name] ?: return DotNetTime.MIN
        val text = (value as? JsonPrimitive)?.takeIf { it.isString }?.content ?: throw IllegalArgumentException(name)
        return GoalJson.parseTime(text)
    }
}
