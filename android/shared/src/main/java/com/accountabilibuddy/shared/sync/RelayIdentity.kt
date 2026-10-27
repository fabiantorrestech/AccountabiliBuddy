package com.accountabilibuddy.shared.sync

import java.net.IDN
import java.net.InetAddress
import java.net.URI

/**
 * What names a relay when a group id and bearer token are derived for it ([SyncKey.relayGroupIdFor]), the same as on
 * the PC (Core's RelayIdentity): its certificate pin when it has one ("pin:" and the 43 characters), else its address
 * as an origin ("origin:https://host" or "origin:https://host:port", host in lower case, an international name as its
 * ASCII form, the port left out when it is 443, an IPv6 address as eight hex groups without zero compression in
 * brackets). The pin comes first because one relay is reached by several addresses (a PC's own relay by 127.0.0.1, a
 * phone by its LAN address) and every device must end up in the same group.
 */
object RelayIdentity {
    private const val DEFAULT_PORT = 443

    /**
     * @param address The relay's https address as the app's RelayAddress.normalize returns it.
     * @param pin The relay certificate's pin, or null for a relay with a normal certificate.
     * @return Null only when an address without a pin has no usable host.
     */
    fun of(address: String, pin: String?): String? {
        if (pin != null) {
            return "pin:$pin"
        }

        val uri = runCatching { URI(address) }.getOrNull() ?: return null
        val rawHost = uri.host?.takeIf { it.isNotBlank() } ?: return null
        val host = if (rawHost.startsWith("[")) {
            ipv6Groups(rawHost.removePrefix("[").removeSuffix("]")) ?: return null
        } else {
            IDN.toASCII(rawHost).lowercase()
        }

        val port = if (uri.port > 0 && uri.port != DEFAULT_PORT) ":${uri.port}" else ""
        return "origin:https://$host$port"
    }

    private fun ipv6Groups(bareAddress: String): String? {
        val withoutZone = bareAddress.substringBefore('%')
        val bytes = runCatching { InetAddress.getByName(withoutZone).address }.getOrNull()
        if (bytes == null || bytes.size != 16) {
            return null
        }

        val groups = (0 until 8).map { ((bytes[2 * it].toInt() and 0xFF) shl 8 or (bytes[2 * it + 1].toInt() and 0xFF)).toString(16) }
        return "[" + groups.joinToString(":") + "]"
    }
}
