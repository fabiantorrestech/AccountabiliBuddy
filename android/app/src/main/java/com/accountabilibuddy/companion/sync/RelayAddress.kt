package com.accountabilibuddy.companion.sync

import java.net.URI

/** The relay's address and pin as typed or scanned (ADR 46): https only, so nothing readable ever travels in the clear. */
object RelayAddress {
    private const val PIN_LENGTH = 43

    /**
     * The address as `https://host[:port]` without a path, or null when it isn't one. A bare `host:port` gets
     * `https://`; `http://` and any other scheme are refused.
     */
    fun normalize(text: String): String? {
        val typed = text.trim()
        if (typed.isEmpty()) {
            return null
        }

        val withScheme = if (typed.contains("://")) typed else "https://$typed"
        val uri = runCatching { URI(withScheme) }.getOrNull() ?: return null
        if (!uri.scheme.equals("https", ignoreCase = true) || uri.host.isNullOrBlank() || uri.userInfo != null) {
            return null
        }

        val hasPath = !uri.rawPath.isNullOrEmpty() && uri.rawPath != "/"
        if (hasPath || uri.rawQuery != null || uri.rawFragment != null) {
            return null
        }

        val port = if (uri.port > 0) ":${uri.port}" else ""
        return "https://${uri.host.lowercase()}$port"
    }

    /** A certificate pin: a SHA-256 in base64url without padding, 43 characters. */
    fun isPin(text: String): Boolean =
        text.length == PIN_LENGTH && text.all { (it in 'a'..'z') || (it in 'A'..'Z') || (it in '0'..'9') || it == '-' || it == '_' }
}
