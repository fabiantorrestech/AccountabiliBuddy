package com.accountabilibuddy.companion.link

import android.net.Uri

/** What the PC's QR code carries (ADR 14): its address and port, its certificate's fingerprint, the one-time code and its name. */
data class PairingLink(val host: String, val port: Int, val fingerprint: String, val code: String, val pcName: String) {
    /** The code grouped for reading, as the PC shows it ("ABCD-EF23"). */
    val displayCode: String get() = if (code.length == 8) "${code.take(4)}-${code.drop(4)}" else code

    companion object {
        const val SCHEME = "accountabilibuddy"
        const val HOST = "pair"

        /** Null when [uri] isn't a complete pairing link. */
        fun parse(uri: Uri): PairingLink? {
            if (uri.scheme != SCHEME || uri.host != HOST) {
                return null
            }

            val host = uri.getQueryParameter("h")?.takeIf { it.isNotBlank() } ?: return null
            val port = uri.getQueryParameter("p")?.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
            val fingerprint = uri.getQueryParameter("f")?.takeIf { it.length >= 40 } ?: return null
            val code = uri.getQueryParameter("c")?.takeIf { it.length == 8 } ?: return null
            return PairingLink(host, port, fingerprint, code, uri.getQueryParameter("n")?.takeIf { it.isNotBlank() } ?: "PC")
        }

        /** A link pasted as text (e.g. copied from the camera's result). */
        fun parse(text: String): PairingLink? = runCatching { parse(Uri.parse(text.trim())) }.getOrNull()
    }
}
