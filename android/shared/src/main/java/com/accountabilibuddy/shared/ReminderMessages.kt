package com.accountabilibuddy.shared

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put
import java.time.Instant

/**
 * Bedtime reminders between devices (ADR 38): the phone tells the watch to ring ([Ring], "/reminder"), and any device
 * tells the others a reminder was dismissed or snoozed ([Action]: "/reminder-action" between phone and watch,
 * `reminderActions` in the sync files between the phone and the PCs).
 */
object ReminderMessages {
    const val DISMISS = "dismiss"
    const val SNOOZE = "snooze"

    /** Ring reminder [number] of [count] tonight ([night] = its evening, "2026-10-25"); [alarm] = until dismissed. */
    data class Ring(val night: String, val number: Int, val count: Int, val label: String, val text: String, val alarm: Boolean)

    /** Reminder [number] of [night] was dismissed or snoozed at [at] (a snooze rings again at [until]). */
    data class Action(val night: String, val number: Int, val action: String, val at: Instant, val until: Instant? = null) {
        val key: String get() = "$night/$number/$action/${at.toEpochMilli()}"
    }

    fun write(ring: Ring): ByteArray = buildJsonObject {
        put("night", ring.night)
        put("number", ring.number)
        put("count", ring.count)
        put("label", ring.label)
        put("text", ring.text)
        put("alarm", ring.alarm)
    }.toString().toByteArray()

    fun readRing(bytes: ByteArray): Ring? = runCatching {
        val o = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        Ring(
            (o["night"] as JsonPrimitive).content, (o["number"] as JsonPrimitive).intOrNull ?: 0, (o["count"] as JsonPrimitive).intOrNull ?: 0,
            (o["label"] as JsonPrimitive).content, (o["text"] as JsonPrimitive).content, (o["alarm"] as? JsonPrimitive)?.booleanOrNull ?: false,
        )
    }.getOrNull()

    fun write(action: Action): ByteArray = toJson(action).toString().toByteArray()

    fun toJson(action: Action): JsonObject = buildJsonObject {
        put("night", action.night)
        put("number", action.number)
        put("action", action.action)
        put("at", action.at.toString())
        action.until?.let { put("until", it.toString()) }
    }

    fun readAction(bytes: ByteArray): Action? = runCatching { fromJson(Json.parseToJsonElement(bytes.decodeToString()) as JsonObject) }.getOrNull()

    fun fromJson(o: JsonObject): Action? = runCatching {
        Action(
            (o["night"] as JsonPrimitive).content, (o["number"] as JsonPrimitive).intOrNull ?: 0, (o["action"] as JsonPrimitive).content,
            Instant.parse((o["at"] as JsonPrimitive).content), (o["until"] as? JsonPrimitive)?.content?.let(Instant::parse),
        )
    }.getOrNull()?.takeIf { it.action == DISMISS || it.action == SNOOZE }
}
