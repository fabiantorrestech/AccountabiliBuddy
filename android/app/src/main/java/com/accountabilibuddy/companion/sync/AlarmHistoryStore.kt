package com.accountabilibuddy.companion.sync

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant

/** The alarms the phone saw as next ([WakeAlarm]), kept in alarms.json in the app's private folder (times and app ids only). */
class AlarmHistoryStore(private val file: File) {
    @Volatile
    var history: List<AlarmSighting> = load()
        private set

    @Synchronized
    fun save(sightings: List<AlarmSighting>) {
        history = sightings
        val json = JsonArray(sightings.map { sighting ->
            buildJsonObject {
                put("at", sighting.at.toEpochMilli())
                put("app", sighting.app)
                put("seenAt", sighting.seenAt.toEpochMilli())
            }
        })
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    private fun load(): List<AlarmSighting> {
        val array = runCatching { Json.parseToJsonElement(file.readText()) as JsonArray }.getOrNull() ?: return emptyList()
        return array.mapNotNull { element ->
            val json = element as? JsonObject ?: return@mapNotNull null
            val at = (json["at"] as? JsonPrimitive)?.longOrNull ?: return@mapNotNull null
            val seenAt = (json["seenAt"] as? JsonPrimitive)?.longOrNull ?: at
            AlarmSighting(Instant.ofEpochMilli(at), (json["app"] as? JsonPrimitive)?.contentOrNull, Instant.ofEpochMilli(seenAt))
        }
    }
}
