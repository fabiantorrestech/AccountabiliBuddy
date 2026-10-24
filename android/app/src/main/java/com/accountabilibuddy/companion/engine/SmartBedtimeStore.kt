package com.accountabilibuddy.companion.engine

import android.content.Context
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * What the smart bedtime learned (ADR 34), in prefs "smartBedtime" (part of the settings backup): each kind's level,
 * the measured nights (60 days) and the bedtime asked each evening. Its switches and limits are in the shared sleep
 * settings (ADR 35).
 */
class SmartBedtimeStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /**
     * The switches and limits kept here before they moved into the shared sleep settings (ADR 35): handed over once,
     * then removed. Null when there's nothing to hand over.
     */
    fun takeOldSettings(): OldSettings? {
        if (!prefs.contains(KEY_WORKDAYS) && !prefs.contains(KEY_OFF_DAYS) && !prefs.contains(KEY_MIN) && !prefs.contains(KEY_MAX)) return null
        val old = OldSettings(prefs.getBoolean(KEY_WORKDAYS, false), prefs.getBoolean(KEY_OFF_DAYS, false), prefs.getFloat(KEY_MIN, 6.5f).toDouble(), prefs.getFloat(KEY_MAX, 9f).toDouble())
        prefs.edit().remove(KEY_WORKDAYS).remove(KEY_OFF_DAYS).remove(KEY_MIN).remove(KEY_MAX).apply()
        return old
    }

    /** What [takeOldSettings] hands over. */
    data class OldSettings(val workdays: Boolean, val offDays: Boolean, val minHours: Double, val maxHours: Double)

    fun level(kind: NightKind): SmartLevel = readObject(KEY_LEVELS)?.get(kind.name)?.let { element ->
        val o = element.jsonObject
        SmartLevel(
            o["level"]?.jsonPrimitive?.intOrNull,
            o["start"]?.jsonPrimitive?.intOrNull,
            o["missed"]?.jsonPrimitive?.intOrNull ?: 0,
            o["lastStep"]?.jsonPrimitive?.content?.let { runCatching { LocalDate.parse(it) }.getOrNull() },
        )
    } ?: SmartLevel()

    fun saveLevel(kind: NightKind, level: SmartLevel) {
        val all = readObject(KEY_LEVELS)?.toMutableMap() ?: mutableMapOf()
        all[kind.name] = buildJsonObject {
            level.levelMinutes?.let { put("level", it) }
            level.startMinutes?.let { put("start", it) }
            put("missed", level.missedWeeks)
            level.lastStep?.let { put("lastStep", it.toString()) }
        }
        prefs.edit().putString(KEY_LEVELS, JsonObject(all).toString()).apply()
    }

    /** Learning starts over for [kind] (e.g. after the switch was off a long time). */
    fun resetLevel(kind: NightKind) = saveLevel(kind, SmartLevel())

    fun nights(): List<NightRecord> = (readElement(KEY_NIGHTS) as? JsonArray).orEmpty().mapNotNull { element ->
        runCatching {
            val o = element.jsonObject
            NightRecord(
                LocalDate.parse(o.getValue("evening").jsonPrimitive.content),
                NightKind.valueOf(o.getValue("kind").jsonPrimitive.content),
                LocalDateTime.parse(o.getValue("asleep").jsonPrimitive.content),
                LocalDateTime.parse(o.getValue("woke").jsonPrimitive.content),
                o.getValue("slept").jsonPrimitive.content.toInt(),
                o["phoneDown"]?.jsonPrimitive?.content?.let(LocalDateTime::parse),
                o["asked"]?.jsonPrimitive?.content?.let(LocalDateTime::parse),
            )
        }.getOrNull()
    }

    /** Keeps the last 60 days. */
    fun saveNights(nights: List<NightRecord>, today: LocalDate) {
        val kept = nights.filter { !it.evening.isBefore(today.minusDays(60)) }.sortedBy { it.evening }
        prefs.edit().putString(KEY_NIGHTS, buildJsonArray {
            kept.forEach { night ->
                add(buildJsonObject {
                    put("evening", night.evening.toString())
                    put("kind", night.kind.name)
                    put("asleep", night.asleep.toString())
                    put("woke", night.woke.toString())
                    put("slept", night.sleptMinutes)
                    night.phoneDown?.let { put("phoneDown", it.toString()) }
                    night.asked?.let { put("asked", it.toString()) }
                })
            }
        }.toString()).apply()
    }

    fun asked(evening: LocalDate): LocalDateTime? =
        readObject(KEY_ASKED)?.get(evening.toString())?.jsonPrimitive?.content?.let { runCatching { LocalDateTime.parse(it) }.getOrNull() }

    /** The bedtime asked for [evening]; the last 60 evenings are kept. */
    fun saveAsked(evening: LocalDate, bedtime: LocalDateTime) {
        val all = (readObject(KEY_ASKED)?.toMutableMap() ?: mutableMapOf())
        all[evening.toString()] = JsonPrimitive(bedtime.toString())
        val kept = all.filterKeys { key -> runCatching { !LocalDate.parse(key).isBefore(evening.minusDays(60)) }.getOrDefault(false) }
        prefs.edit().putString(KEY_ASKED, JsonObject(kept).toString()).apply()
    }

    private fun readElement(key: String) = prefs.getString(key, null)?.let { runCatching { Json.parseToJsonElement(it) }.getOrNull() }

    private fun readObject(key: String): JsonObject? = readElement(key) as? JsonObject

    companion object {
        const val PREFS = "smartBedtime"
        private const val KEY_WORKDAYS = "workdays"
        private const val KEY_OFF_DAYS = "offDays"
        private const val KEY_MIN = "minHours"
        private const val KEY_MAX = "maxHours"
        private const val KEY_LEVELS = "levels"
        private const val KEY_NIGHTS = "nights"
        private const val KEY_ASKED = "asked"
    }
}
