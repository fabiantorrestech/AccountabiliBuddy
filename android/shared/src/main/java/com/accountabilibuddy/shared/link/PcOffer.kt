package com.accountabilibuddy.shared.link

import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put

/**
 * What the PC's pairing QR code carries (ADR 14), as the phone passes it to the watch (ADR 21: a watch can't scan a QR
 * code): the PC's address and port, its certificate's fingerprint, the one-time code and its name.
 */
data class PcOffer(val host: String, val port: Int, val fingerprint: String, val code: String, val pcName: String) {
    companion object {
        fun write(offer: PcOffer): JsonObject = buildJsonObject {
            put("host", offer.host)
            put("port", offer.port)
            put("fingerprint", offer.fingerprint)
            put("code", offer.code)
            put("pcName", offer.pcName)
        }

        /** Null when a part is missing. */
        fun read(json: JsonObject): PcOffer? {
            fun string(name: String): String? = (json[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content
            return PcOffer(
                host = string("host") ?: return null,
                port = (json["port"] as? JsonPrimitive)?.intOrNull ?: return null,
                fingerprint = string("fingerprint") ?: return null,
                code = string("code") ?: return null,
                pcName = string("pcName") ?: "PC",
            )
        }
    }
}
