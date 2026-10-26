package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.model.BlockLift
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.model.BlockRules
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalTime
import java.time.format.DateTimeFormatter

/** A day template's "blocks" (schema v10, ADR 37), the same JSON as the PC's System.Text.Json camelCase. */
object BlockRulesJson {
    private val time = DateTimeFormatter.ofPattern("HH:mm:ss")

    fun write(rules: BlockRules): JsonObject = buildJsonObject {
        rules.guarded?.let { put("guarded", refs(it)) }
        put("reenableAt", rules.reenableAt.format(time))
        rules.warnMinutes?.let { put("warnMinutes", it) }
        put("lifts", buildJsonArray {
            rules.lifts.forEach { lift ->
                add(buildJsonObject {
                    put("start", lift.start.format(time))
                    put("end", lift.end.format(time))
                    put("mode", lift.mode)
                    put("blocks", refs(lift.blocks))
                })
            }
        })
    }

    fun read(json: JsonObject): BlockRules = BlockRules(
        guarded = (json["guarded"] as? JsonArray)?.let(::readRefs),
        reenableAt = (json["reenableAt"] as? JsonPrimitive)?.content?.let(LocalTime::parse) ?: LocalTime.MIDNIGHT,
        warnMinutes = (json["warnMinutes"]?.takeIf { it !is JsonNull } as? JsonPrimitive)?.intOrNull,
        lifts = (json["lifts"] as? JsonArray).orEmpty().map {
            val lift = it.jsonObject
            BlockLift(
                start = LocalTime.parse(lift["start"]!!.jsonPrimitive.content),
                end = LocalTime.parse(lift["end"]!!.jsonPrimitive.content),
                mode = (lift["mode"] as? JsonPrimitive)?.content ?: BlockLift.ALLOW,
                blocks = (lift["blocks"] as? JsonArray)?.let(::readRefs).orEmpty(),
            )
        },
    )

    fun refs(list: List<BlockRef>): JsonArray = buildJsonArray {
        list.forEach { ref ->
            add(buildJsonObject {
                put("device", ref.device)
                put("kind", ref.kind)
                put("id", ref.id)
                put("name", ref.name)
            })
        }
    }

    fun readRefs(array: JsonArray): List<BlockRef> = array.mapNotNull { element ->
        val o = element as? JsonObject ?: return@mapNotNull null
        BlockRef(
            o["device"]?.jsonPrimitive?.content ?: return@mapNotNull null,
            o["kind"]?.jsonPrimitive?.content ?: return@mapNotNull null,
            o["id"]?.jsonPrimitive?.content ?: return@mapNotNull null,
            o["name"]?.jsonPrimitive?.content ?: "",
        )
    }
}
