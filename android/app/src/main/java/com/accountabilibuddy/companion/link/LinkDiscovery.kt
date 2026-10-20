package com.accountabilibuddy.companion.link

import android.content.Context
import android.net.ConnectivityManager
import android.os.SystemClock
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.PortUnreachableException
import java.net.SocketTimeoutException
import java.nio.ByteBuffer

/**
 * Finds the paired PC when it isn't at its remembered address (ADR 14): one UDP broadcast on the local network with the
 * PC's certificate fingerprint; the PC answers with its link port, and its address is where the answer came from.
 * Nothing is trusted from this: the connection still pins the certificate and signs in with the token.
 */
class LinkDiscovery(private val context: Context) {
    data class Found(val host: String, val port: Int)

    /** Null when no PC answered in time. Blocks for up to [timeoutMs]: call off the main thread. */
    fun find(fingerprint: String, port: Int, timeoutMs: Int = TIMEOUT_MS): Found? {
        val request = buildJsonObject {
            put("t", "where")
            put("f", fingerprint)
        }.toString().toByteArray(Charsets.UTF_8)

        return try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = timeoutMs
                for (target in broadcastAddresses()) {
                    runCatching { socket.send(DatagramPacket(request, request.size, target, port)) }
                }

                val deadline = SystemClock.elapsedRealtime() + timeoutMs
                val buffer = ByteArray(MAX_ANSWER_BYTES)
                while (SystemClock.elapsedRealtime() < deadline) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        socket.receive(packet)
                    } catch (e: SocketTimeoutException) {
                        return null
                    } catch (e: PortUnreachableException) {
                        continue
                    }

                    val answer = runCatching { Json.parseToJsonElement(String(packet.data, 0, packet.length, Charsets.UTF_8)) as JsonObject }
                        .getOrNull() ?: continue
                    val linkPort = answer.int("p")
                    val host = packet.address?.hostAddress
                    if (answer.string("t") == "here" && linkPort != null && host != null) {
                        return Found(host, linkPort)
                    }
                }

                null
            }
        } catch (e: IOException) {
            null
        }
    }

    /** 255.255.255.255 and the current network's own broadcast address (some networks drop one of them). */
    private fun broadcastAddresses(): List<InetAddress> {
        val addresses = mutableListOf(InetAddress.getByName("255.255.255.255"))
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        connectivity.getLinkProperties(connectivity.activeNetwork)?.linkAddresses?.forEach { link ->
            val address = link.address as? Inet4Address ?: return@forEach
            val prefix = link.prefixLength.coerceIn(0, 32)
            val hostBits = if (prefix == 32) 0 else (-1 ushr prefix)
            val broadcast = ByteBuffer.wrap(address.address).int or hostBits
            addresses.add(InetAddress.getByAddress(ByteBuffer.allocate(4).putInt(broadcast).array()))
        }

        return addresses.distinct()
    }

    companion object {
        private const val TIMEOUT_MS = 1_500
        private const val MAX_ANSWER_BYTES = 512
    }
}
