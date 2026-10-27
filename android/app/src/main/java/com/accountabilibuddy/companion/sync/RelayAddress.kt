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
    fun isPin(text: String): Boolean = isBase64Url43(text)

    /** A relay's admission key: 32 random bytes in base64url without padding, 43 characters. */
    fun isAdmissionKey(text: String): Boolean = isBase64Url43(text)

    private fun isBase64Url43(text: String): Boolean =
        text.length == PIN_LENGTH && text.all { (it in 'a'..'z') || (it in 'A'..'Z') || (it in '0'..'9') || it == '-' || it == '_' }

    /**
     * Whether the relay is somewhere a person might run their own: a local-network address (10.x, 192.168.x, 172.16-31.x,
     * 169.254.x, 127.x), a CGNAT/Tailscale one (100.64-127.x), a name ending in ".local" or ".lan" or without a dot (none of those
     * resolve on the public internet), or an IPv6 link-local or unique-local address. Every other DNS name counts as the internet,
     * "*.ts.net" included: a Tailscale Funnel name is public.
     */
    fun isLocalNetwork(url: String): Boolean {
        val host = runCatching { URI(url).host }.getOrNull()?.lowercase()?.removePrefix("[")?.removeSuffix("]") ?: return false
        if (host == "localhost" || host.endsWith(".local") || host.endsWith(".lan")) {
            return true
        }

        val parts = host.split('.')
        val numbers = parts.map { it.toIntOrNull() }
        if (parts.size == 4 && numbers.all { it != null && it in 0..255 }) {
            val a = numbers[0]!!
            val b = numbers[1]!!
            return a == 10 || a == 127 || (a == 192 && b == 168) || (a == 172 && b in 16..31) || (a == 169 && b == 254) ||
                (a == 100 && b in 64..127)
        }

        if (host.contains(':')) {
            return host == "::1" || host.startsWith("fe80:") || host.startsWith("fc") || host.startsWith("fd")
        }

        return !host.contains('.')
    }
}
