package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncKeyCode
import java.net.URI

/**
 * The PC's link code as a QR code (ADR 22): `accountabilibuddy://join?c=<40-character code>&n=<PC name>`. Scanning it
 * asks "Join <PC name>'s devices?" before the phone joins the group.
 */
data class JoinLink(val code: String, val pcName: String) {
    companion object {
        const val HOST = "join"

        /** Null when [text] isn't a join link with a valid code. */
        fun parse(text: String): JoinLink? {
            val uri = runCatching { URI(text.trim()) }.getOrNull() ?: return null
            if (!uri.scheme.equals("accountabilibuddy", ignoreCase = true) || !uri.host.equals(HOST, ignoreCase = true)) {
                return null
            }

            val query = (uri.rawQuery ?: return null).split('&')
                .mapNotNull { pair -> pair.split('=', limit = 2).takeIf { it.size == 2 } }
                .associate { (name, value) -> name to java.net.URLDecoder.decode(value, Charsets.UTF_8) }
            val code = query["c"]?.takeIf { SyncKeyCode.parse(it) != null } ?: return null
            return JoinLink(code, query["n"]?.takeIf { it.isNotBlank() } ?: "your other device")
        }
    }
}
