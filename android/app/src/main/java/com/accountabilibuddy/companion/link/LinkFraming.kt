package com.accountabilibuddy.companion.link

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.EOFException
import java.io.IOException

/** Messages on the link: a 4-byte big-endian length, then UTF-8 JSON `{type, body}` (as on the PC). */
object LinkFraming {
    private const val MAX_FRAME_BYTES = 1024 * 1024

    fun write(output: DataOutputStream, type: String, body: JsonElement? = null) {
        val envelope = buildJsonObject {
            put("type", type)
            if (body != null) {
                put("body", body)
            }
        }
        val bytes = envelope.toString().toByteArray(Charsets.UTF_8)
        if (bytes.size > MAX_FRAME_BYTES) {
            throw IOException("The message is too large for the link.")
        }

        output.writeInt(bytes.size)
        output.write(bytes)
        output.flush()
    }

    /** Null when the PC closed the connection. */
    fun read(input: DataInputStream): LinkEnvelope? {
        val length = try {
            input.readInt()
        } catch (e: EOFException) {
            return null
        }

        if (length <= 0 || length > MAX_FRAME_BYTES) {
            throw IOException("The message length isn't valid.")
        }

        val bytes = ByteArray(length)
        try {
            input.readFully(bytes)
        } catch (e: EOFException) {
            return null
        }

        val json = runCatching { Json.parseToJsonElement(bytes.toString(Charsets.UTF_8)) as JsonObject }
            .getOrElse { throw IOException("The message isn't valid JSON.") }
        val type = json["type"]?.jsonPrimitive?.content ?: throw IOException("The message has no type.")
        return LinkEnvelope(type, json["body"]?.takeIf { it !is JsonNull })
    }
}
