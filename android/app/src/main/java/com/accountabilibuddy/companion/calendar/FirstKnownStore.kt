package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.FirstKnownEntry
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant
import java.time.LocalDateTime
import java.util.UUID

/**
 * When this phone first saw each classified calendar event at its current time and category (ADR 58), kept in a file of its own
 * ("companion/calendar-first-known.json") and never synced, backed up or sent to the watch. It holds ids, times and a category id: no
 * titles. The list is held in memory; a change is written by [save], which the caller runs off the main thread. A file that can't be
 * read starts empty, which is the strict way: every event is then new and has to be held for the notice again.
 */
class FirstKnownStore(private val file: File) {
    @Volatile
    var entries: List<FirstKnownEntry> = load()
        private set

    /** Replaces the held entries with [next] (the same instance means nothing changed). True when they changed and [save] is due. */
    @Synchronized
    fun replace(next: List<FirstKnownEntry>): Boolean {
        if (next === entries) return false

        entries = next
        return true
    }

    /** Writes the held entries to the file. Safe from any thread but the caller should not be the main one. */
    @Synchronized
    fun save() {
        val json = buildJsonArray {
            entries.forEach { entry ->
                add(buildJsonObject {
                    put("calendarId", entry.calendarId)
                    put("eventId", entry.eventId)
                    put("start", entry.localStart.toString())
                    put("end", entry.localEnd.toString())
                    put("categoryId", entry.categoryId.toString())
                    put("behavior", entry.behavior.name)
                    put("knownAt", entry.knownAt.toString())
                })
            }
        }
        runCatching {
            file.parentFile?.mkdirs()
            val temporary = File(file.parentFile, file.name + ".tmp")
            temporary.writeText(json.toString())
            if (!temporary.renameTo(file)) {
                file.delete()
                temporary.renameTo(file)
            }
        }
    }

    private fun load(): List<FirstKnownEntry> {
        if (!file.exists()) return emptyList()

        return runCatching {
            (Json.parseToJsonElement(file.readText()) as JsonArray).mapNotNull { element ->
                val o = element as? JsonObject ?: return@mapNotNull null
                fun text(name: String) = (o[name] as? JsonPrimitive)?.contentOrNull
                runCatching {
                    FirstKnownEntry(
                        text("calendarId")!!, text("eventId")!!, LocalDateTime.parse(text("start")!!), LocalDateTime.parse(text("end")!!),
                        UUID.fromString(text("categoryId")!!), CategoryBehavior.valueOf(text("behavior")!!), Instant.parse(text("knownAt")!!),
                    )
                }.getOrNull()
            }
        }.getOrDefault(emptyList())
    }
}
