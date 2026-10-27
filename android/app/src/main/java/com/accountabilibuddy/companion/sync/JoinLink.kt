package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncKeyCode
import java.net.URI

/**
 * The PC's link code as a QR code (ADR 22): `accountabilibuddy://join?c=<40-character code>&n=<PC name>`. Scanning it
 * asks "Join <PC name>'s devices?" before the phone joins the group. A device using the user's own relay (ADR 46) adds
 * `&r=<https address>`, for a self-signed certificate `&p=<43-character SHA-256 pin>` and, when the relay wants one for
 * a new group, `&k=<43-character admission key>`, so the phone sets the relay up too. The link is not proof of who made it:
 * the confirmation shows what it changes (the relay's host, leaving a group) and the name in it is only a label.
 */
data class JoinLink(
    val code: String,
    val pcName: String,
    /** The relay's address, or null when the link doesn't carry one. */
    val relayUrl: String? = null,
    /** The relay certificate's pin; null with an address means a normal certificate authority check. */
    val relayPin: String? = null,
    /** The relay's admission key (a secret that lets this phone start the group on the relay); null when the link has none. */
    val relayAdmission: String? = null,
) {
    /** The relay's host and port for the confirmation ("192.168.1.20:8743"), or null when the link has no relay. */
    val relayHost: String?
        get() = relayUrl?.removePrefix("https://")

    companion object {
        const val HOST = "join"

        /** Null when [text] isn't a join link with a valid code (and, if it names a relay, a valid address and pin). */
        fun parse(text: String): JoinLink? {
            val uri = runCatching { URI(text.trim()) }.getOrNull() ?: return null
            if (!uri.scheme.equals("accountabilibuddy", ignoreCase = true) || !uri.host.equals(HOST, ignoreCase = true)) {
                return null
            }

            val query = (uri.rawQuery ?: return null).split('&')
                .mapNotNull { pair -> pair.split('=', limit = 2).takeIf { it.size == 2 } }
                .associate { (name, value) -> name to java.net.URLDecoder.decode(value, Charsets.UTF_8) }
            val code = query["c"]?.takeIf { SyncKeyCode.parse(it) != null } ?: return null
            val name = query["n"]?.takeIf { it.isNotBlank() } ?: "your other device"
            val address = query["r"]
            val pin = query["p"]
            val admission = query["k"]
            if (address == null) {
                // A pin or key without an address means nothing; refuse the link rather than guess.
                return if (pin == null && admission == null) JoinLink(code, name) else null
            }

            // A relay that can't be used as given (not https, a malformed pin) refuses the whole link: dropping the pin
            // would silently check the certificate another way.
            val relayUrl = RelayAddress.normalize(address) ?: return null
            if (pin != null && !RelayAddress.isPin(pin)) {
                return null
            }

            if (admission != null && !RelayAddress.isAdmissionKey(admission)) {
                return null
            }

            return JoinLink(code, name, relayUrl, pin, admission)
        }
    }
}
