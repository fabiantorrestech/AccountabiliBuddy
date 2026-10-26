package com.accountabilibuddy.shared

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put

/**
 * The phone's noise player as the watch sees and controls it (ADR 36): the phone sends [State] ("/noise") when it
 * changes, the watch sends a command ("/noise-command") back. Only the shown name, playing and the volume reach the
 * watch.
 */
object NoiseRemote {
    const val TOGGLE = "toggle"
    const val NEXT = "next"
    const val PREVIOUS = "previous"
    const val VOLUME = "volume"

    /**
     * What's playing on the phone. [title] is the sound's shown name; null when no folder is chosen. [locked]: the
     * player is locked to headphones and none are connected, so play does nothing (false when absent).
     */
    data class State(val playing: Boolean, val title: String?, val volume: Int, val hasFolder: Boolean, val locked: Boolean = false)

    /** A command from the watch: [action] is one of the constants; [volume] 0-100 for [VOLUME]. */
    data class Command(val action: String, val volume: Int? = null)

    fun write(state: State): ByteArray = buildJsonObject {
        put("playing", state.playing)
        state.title?.let { put("title", it) }
        put("volume", state.volume)
        put("hasFolder", state.hasFolder)
        put("locked", state.locked)
    }.toString().toByteArray()

    fun readState(bytes: ByteArray): State? = runCatching {
        val json = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        State(
            (json["playing"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (json["title"] as? JsonPrimitive)?.takeIf { it.isString }?.content,
            (json["volume"] as? JsonPrimitive)?.intOrNull ?: 40,
            (json["hasFolder"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (json["locked"] as? JsonPrimitive)?.booleanOrNull ?: false,
        )
    }.getOrNull()

    fun write(command: Command): ByteArray = buildJsonObject {
        put("action", command.action)
        command.volume?.let { put("volume", it) }
    }.toString().toByteArray()

    fun readCommand(bytes: ByteArray): Command? = runCatching {
        val json = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        val action = (json["action"] as JsonPrimitive).content
        Command(action, (json["volume"] as? JsonPrimitive)?.intOrNull?.coerceIn(0, 100))
    }.getOrNull()?.takeIf { it.action in setOf(TOGGLE, NEXT, PREVIOUS, VOLUME) }
}
