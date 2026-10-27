package com.accountabilibuddy.shared

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put

/**
 * The phone's noise player as the watch sees and controls it (ADR 36): the phone sends [State] ("/noise") when it
 * changes, the watch sends a command ("/noise-command") back. Only the shown name, playing and the volume reach the
 * watch. Since ADR 42 the state also lists the user's PCs that have a noise player ([State.devices], read from their
 * sync files by the phone) and a command can be for one of them ([Command.device]): the phone passes it on to that PC
 * through its own sync file, so the answer takes a few seconds (longer when the PC is idle).
 */
object NoiseRemote {
    const val TOGGLE = "toggle"
    const val NEXT = "next"
    const val PREVIOUS = "previous"
    const val VOLUME = "volume"

    /**
     * What's playing on the phone. [title] is the sound's shown name; null when no folder is chosen. [locked]: the
     * player is locked to headphones and none are connected, so play does nothing (false when absent). [devices]: the
     * PCs whose files carry a noise player (ADR 42); empty for none or from a phone that predates them.
     */
    data class State(
        val playing: Boolean,
        val title: String?,
        val volume: Int,
        val hasFolder: Boolean,
        val locked: Boolean = false,
        val devices: List<PcPlayer> = emptyList(),
    )

    /**
     * One PC's noise player as its last file said (a few seconds old at best). [id] is the PC's sync device id, what a
     * command names. [sending]: a command for it is still waiting for the PC to answer. [note]: the PC's last refusal,
     * or "Update AccountabiliBuddy on <PC>", shown for a few seconds.
     */
    data class PcPlayer(
        val id: String,
        val name: String,
        val playing: Boolean,
        val title: String?,
        val volume: Int,
        val locked: Boolean,
        val sending: Boolean = false,
        val note: String? = null,
    )

    /**
     * A command from the watch: [action] is one of the constants; [volume] 0-100 for [VOLUME]; [device] the PC's sync
     * device id when the command is for a PC, null for this phone.
     */
    data class Command(val action: String, val volume: Int? = null, val device: String? = null)

    fun write(state: State): ByteArray = buildJsonObject {
        put("playing", state.playing)
        state.title?.let { put("title", it) }
        put("volume", state.volume)
        put("hasFolder", state.hasFolder)
        put("locked", state.locked)
        if (state.devices.isNotEmpty()) {
            put("devices", JsonArray(state.devices.map { pc ->
                buildJsonObject {
                    put("id", pc.id)
                    put("name", pc.name)
                    put("playing", pc.playing)
                    pc.title?.let { put("title", it) }
                    put("volume", pc.volume)
                    put("locked", pc.locked)
                    put("sending", pc.sending)
                    pc.note?.let { put("note", it) }
                }
            }))
        }
    }.toString().toByteArray()

    fun readState(bytes: ByteArray): State? = runCatching {
        val json = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        State(
            (json["playing"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (json["title"] as? JsonPrimitive)?.takeIf { it.isString }?.content,
            (json["volume"] as? JsonPrimitive)?.intOrNull ?: 40,
            (json["hasFolder"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (json["locked"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (json["devices"] as? JsonArray).orEmpty().mapNotNull(::readPc),
        )
    }.getOrNull()

    // A PC that can't be read is left out rather than losing the phone's own player.
    private fun readPc(element: JsonElement): PcPlayer? = runCatching {
        val pc = element as JsonObject
        fun text(key: String) = (pc[key] as? JsonPrimitive)?.takeIf { it.isString }?.content
        PcPlayer(
            text("id") ?: return@runCatching null,
            text("name") ?: "PC",
            (pc["playing"] as? JsonPrimitive)?.booleanOrNull ?: false,
            text("title"),
            ((pc["volume"] as? JsonPrimitive)?.intOrNull ?: 0).coerceIn(0, 100),
            (pc["locked"] as? JsonPrimitive)?.booleanOrNull ?: false,
            (pc["sending"] as? JsonPrimitive)?.booleanOrNull ?: false,
            text("note"),
        )
    }.getOrNull()

    fun write(command: Command): ByteArray = buildJsonObject {
        put("action", command.action)
        command.volume?.let { put("volume", it) }
        command.device?.let { put("device", it) }
    }.toString().toByteArray()

    fun readCommand(bytes: ByteArray): Command? = runCatching {
        val json = Json.parseToJsonElement(bytes.decodeToString()) as JsonObject
        val action = (json["action"] as JsonPrimitive).content
        Command(
            action,
            (json["volume"] as? JsonPrimitive)?.intOrNull?.coerceIn(0, 100),
            (json["device"] as? JsonPrimitive)?.takeIf { it.isString }?.content,
        )
    }.getOrNull()?.takeIf { it.action in setOf(TOGGLE, NEXT, PREVIOUS, VOLUME) }
}
