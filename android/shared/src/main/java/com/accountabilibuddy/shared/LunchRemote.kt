package com.accountabilibuddy.shared

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * The watch's Lunch page controls the phone's lunch ("/lunch"): start, back early, +5 or −5 min. The phone applies it
 * to its lunch, which then goes to the other devices in its sync file and back to the watch in the next snapshot.
 */
object LunchRemote {
    const val START = "start"
    const val BACK = "back"
    const val PLUS = "plus"
    const val MINUS = "minus"

    private val actions = setOf(START, BACK, PLUS, MINUS)

    fun write(action: String): ByteArray = buildJsonObject { put("action", action) }.toString().toByteArray()

    /** The action, or null when it isn't one of the four. */
    fun read(bytes: ByteArray): String? = runCatching {
        val json = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        (json["action"] as JsonPrimitive).content
    }.getOrNull()?.takeIf { it in actions }
}
