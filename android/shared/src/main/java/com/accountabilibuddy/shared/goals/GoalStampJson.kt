package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * A goal's `stamps` as the PC writes them: a part is `{"at": time, "by": id}` under its [GoalField.jsonName], in the field order, null
 * parts left out, stamp names of a newer app after them. A part's own unknown fields are dropped (the PC's record has none).
 */
object GoalStampJson {
    private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    fun write(stamps: GoalFieldStamps): JsonObject = buildJsonObject {
        for (field in GoalField.values()) {
            val stamp = stamps.of(field) ?: continue
            put(field.jsonName, buildJsonObject {
                put("at", GoalJson.timeValue(stamp.at))
                put("by", stamp.by)
            })
        }
        stamps.extra.forEach { (key, value) -> put(key, value) }
    }

    /** Reads `stamps`; damaged parts (not an object, a time or id that doesn't read) throw, so the goal is left out as on the PC. */
    fun read(json: JsonElement): GoalFieldStamps {
        val obj = json as? JsonObject ?: throw IllegalArgumentException("Goal JSON: stamps")
        val known = GoalField.values().map { it.jsonName }
        val parts = HashMap<GoalField, FieldStamp>()
        for (field in GoalField.values()) {
            val value = lookup(obj, field.jsonName) ?: continue
            if (value is JsonNull) continue
            parts[field] = readStamp(value)
        }
        val extra = JsonObject(obj.filterKeys { key -> known.none { it.equals(key, ignoreCase = true) } })
        return GoalFieldStamps(parts, extra)
    }

    private fun lookup(obj: JsonObject, name: String): JsonElement? = obj[name] ?: obj.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value

    private fun readStamp(json: JsonElement): FieldStamp {
        val obj = json as? JsonObject ?: throw IllegalArgumentException("Goal JSON: stamp")
        val at: OffsetDateTime = lookup(obj, "at")?.let { time(it) } ?: com.accountabilibuddy.shared.sync.DotNetTime.MIN
        val by: String = lookup(obj, "by")?.let { guid(it) } ?: FieldStamp.NOBODY
        return FieldStamp(at, by)
    }

    private fun time(json: JsonElement): OffsetDateTime {
        val primitive = json as? JsonPrimitive
        if (primitive == null || primitive is JsonNull || !primitive.isString) throw IllegalArgumentException("Goal JSON: stamp time")
        return GoalJson.parseTime(primitive.content)
    }

    private fun guid(json: JsonElement): String {
        val primitive = json as? JsonPrimitive
        if (primitive == null || primitive is JsonNull || !primitive.isString || !guidForm.matches(primitive.content)) {
            throw IllegalArgumentException("Goal JSON: stamp device")
        }
        return UUID.fromString(primitive.content).toString()
    }
}
