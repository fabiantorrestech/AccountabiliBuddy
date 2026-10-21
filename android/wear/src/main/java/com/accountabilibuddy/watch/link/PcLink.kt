package com.accountabilibuddy.watch.link

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Build
import com.accountabilibuddy.shared.FocusDayJson
import com.accountabilibuddy.shared.SnapshotBuilder
import com.accountabilibuddy.shared.SnapshotInput
import com.accountabilibuddy.shared.link.LinkEnvelope
import com.accountabilibuddy.shared.link.LinkFraming
import com.accountabilibuddy.shared.link.LinkStateJson
import com.accountabilibuddy.shared.link.PcOffer
import com.accountabilibuddy.shared.link.PinnedTrustManager
import com.accountabilibuddy.shared.link.TokenVault
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.Closeable
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File
import java.io.IOException
import java.net.InetSocketAddress
import java.time.Instant
import java.time.ZoneId
import javax.net.ssl.SSLSocket

/**
 * The watch's own link to the PC over Wi-Fi (ADR 21), for when the phone can't be reached: the ADR 14 link as a paired
 * device of kind "watch", which gets the PC's session and today's focus and nothing else. The pairing code comes from
 * the PC's QR code through the phone. Wear OS sends traffic through the phone by default, so each use asks for Wi-Fi
 * and lets it go right after.
 */
object PcLink {
    private const val FILE = "pc.json"
    private const val WIFI_WAIT_MS = 10_000
    private const val CONNECT_TIMEOUT_MS = 5_000
    private const val READ_TIMEOUT_MS = 10_000
    private const val PROTOCOL_VERSION = 1

    /** The PC this watch paired with: where it is, its certificate's fingerprint, and the sealed device token. */
    private data class PairedPc(val host: String, val port: Int, val fingerprint: String, val deviceId: String, val sealedToken: String, val pcName: String)

    fun isPaired(context: Context): Boolean = load(context) != null

    /** Pairs with the PC from the offer the phone passed on. Null when paired, else why not. */
    suspend fun pair(context: Context, offer: PcOffer): String? = withContext(Dispatchers.IO) {
        withWifi(context) { network ->
            try {
                pairOn(context, network, offer)
            } catch (e: IOException) {
                "Couldn't reach ${offer.pcName} on Wi-Fi (${e.message ?: "no answer"}). Is the watch on the same network?"
            }
        } ?: "The watch couldn't get on Wi-Fi."
    }

    private fun pairOn(context: Context, network: Network, offer: PcOffer): String? =
        Connection.open(network, offer.host, offer.port, offer.fingerprint).use { connection ->
            connection.send("pair", buildJsonObject {
                put("code", offer.code)
                put("deviceName", Build.MODEL)
                put("deviceKind", "watch")
            })
            val answer = connection.read() ?: return@use "The PC closed the connection."
            val body = answer.body as? JsonObject
            if (answer.type != "paired" || body == null) {
                return@use body?.string("message") ?: "The PC didn't take the code. Show a new QR code and scan it again."
            }

            val deviceId = body.string("deviceId") ?: return@use "The PC's answer was incomplete."
            val token = body.string("token") ?: return@use "The PC's answer was incomplete."
            val pcName = body.string("pcName") ?: offer.pcName
            save(context, PairedPc(offer.host, offer.port, offer.fingerprint, deviceId, TokenVault.seal(token), pcName))
            // The PC goes on with its welcome, state and today's focus: the first snapshot from it.
            readNews(context, connection, pcName)
            null
        }

    /** Gets the PC's session and today's focus once and keeps them as the watch's snapshot. False when it couldn't. */
    suspend fun refresh(context: Context): Boolean = withContext(Dispatchers.IO) {
        val pc = load(context) ?: return@withContext false
        val token = TokenVault.open(pc.sealedToken) ?: return@withContext false
        withWifi(context) { network ->
            Connection.open(network, pc.host, pc.port, pc.fingerprint).use { connection ->
                connection.send("hello", buildJsonObject {
                    put("deviceId", pc.deviceId)
                    put("token", token)
                    put("deviceName", Build.MODEL)
                    put("protocolVersion", PROTOCOL_VERSION)
                })
                readNews(context, connection, pc.pcName)
            }
        } ?: false
    }

    /** Forgets the PC (the watch was unlinked on the phone). The PC still lists the watch until it's unpaired there. */
    fun forget(context: Context) {
        File(context.filesDir, FILE).delete()
    }

    /** Reads until the PC has sent its state and today's focus, then keeps them as a snapshot. */
    private fun readNews(context: Context, connection: Connection, pcName: String): Boolean {
        var state: JsonObject? = null
        var focusDay: JsonObject? = null
        while (state == null || focusDay == null) {
            val message = connection.read() ?: return false
            when (message.type) {
                "state" -> state = message.body as? JsonObject
                "focusDay" -> focusDay = message.body as? JsonObject
                "refused" -> {
                    // This watch isn't paired there any more: what came from that PC goes too.
                    forget(context)
                    SnapshotStore.clear(context)
                    return false
                }
            }
        }

        val now = Instant.now()
        val zone = ZoneId.systemDefault()
        val day = FocusDayJson.read(focusDay)
        val parts = day?.takeIf { it.date == now.atZone(zone).toLocalDate() }?.parts.orEmpty()
        val previous = SnapshotStore.snapshot(context).value
        val snapshot = SnapshotBuilder.build(
            SnapshotInput(now, zone, linked = true, pcName, parts, day, now, LinkStateJson.running(state), pcConnected = true,
                wifiAllowed = previous?.wifiAllowed ?: true),
        )
        SnapshotStore.save(context, snapshot)
        return true
    }

    /** Asks for Wi-Fi, runs [block] on it, and lets it go. Null when the watch couldn't get on Wi-Fi in time. */
    private suspend fun <T> withWifi(context: Context, block: suspend (Network) -> T): T? {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        val available = CompletableDeferred<Network?>()
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                available.complete(network)
            }

            override fun onUnavailable() {
                available.complete(null)
            }
        }

        val request = NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build()
        connectivity.requestNetwork(request, callback, WIFI_WAIT_MS)
        return try {
            val network = withTimeoutOrNull(WIFI_WAIT_MS + 1_000L) { available.await() } ?: return null
            try {
                block(network)
            } catch (e: IOException) {
                null
            }
        } finally {
            runCatching { connectivity.unregisterNetworkCallback(callback) }
        }
    }

    private fun load(context: Context): PairedPc? {
        val file = File(context.filesDir, FILE)
        if (!file.exists()) {
            return null
        }

        val json = runCatching { Json.parseToJsonElement(file.readText()) as? JsonObject }.getOrNull() ?: return null
        return PairedPc(
            host = json.string("host") ?: return null,
            port = (json["port"] as? JsonPrimitive)?.intOrNull ?: return null,
            fingerprint = json.string("fingerprint") ?: return null,
            deviceId = json.string("deviceId") ?: return null,
            sealedToken = json.string("sealedToken") ?: return null,
            pcName = json.string("pcName") ?: "PC",
        )
    }

    private fun save(context: Context, pc: PairedPc) {
        val file = File(context.filesDir, FILE)
        val temporary = File(context.filesDir, "$FILE.tmp")
        temporary.writeText(buildJsonObject {
            put("host", pc.host)
            put("port", pc.port)
            put("fingerprint", pc.fingerprint)
            put("deviceId", pc.deviceId)
            put("sealedToken", pc.sealedToken)
            put("pcName", pc.pcName)
        }.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    private fun JsonObject.string(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    /** One TLS connection over the given network, trusting only the PC's pinned certificate. */
    private class Connection(private val socket: SSLSocket) : Closeable {
        private val input = DataInputStream(BufferedInputStream(socket.inputStream))
        private val output = DataOutputStream(BufferedOutputStream(socket.outputStream))

        fun send(type: String, body: JsonObject?) = LinkFraming.write(output, type, body)

        fun read(): LinkEnvelope? = LinkFraming.read(input)

        override fun close() {
            runCatching { socket.close() }
        }

        companion object {
            fun open(network: Network, host: String, port: Int, fingerprint: String): Connection {
                val raw = network.socketFactory.createSocket()
                raw.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
                raw.soTimeout = READ_TIMEOUT_MS
                val tls = PinnedTrustManager(fingerprint).socketFactory().createSocket(raw, host, port, true) as SSLSocket
                tls.startHandshake()
                return Connection(tls)
            }
        }
    }
}
