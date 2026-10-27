package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.PinnedTrustManager
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.net.URL
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLException

/**
 * Where the user's own relay is and how this phone proves it belongs to the group (ADR 46): the https [baseUrl], the
 * certificate [pin] when it is self-signed (null: a normal certificate authority check), the relay group id and the
 * bearer token. The token only ever goes in the Authorization header; it has no toString, so it can't reach a log.
 */
class RelayEndpoint(val baseUrl: String, val pin: String?, val groupId: String, internal val token: String)

/** A file in the relay's list: its name and the etag (SHA-256, base64url) of its bytes. */
class RelayFileInfo(val name: String, val etag: String)

/** A file the relay handed over, with the etag it had when it did. */
class RelayFile(val bytes: ByteArray, val etag: String?)

/** Why a relay call failed, for the status line. A pin that doesn't match is [Certificate] and is never retried without the pin. */
enum class RelayProblem { Address, Unreachable, Certificate, Unauthorized, OtherGroup, Busy, Refused, Server }

/** A relay call that didn't work. The message never holds a body or a token. */
class RelayException(val problem: RelayProblem, message: String) : IOException(message)

/**
 * The phone's only calls to the user's own relay (ADR 46), over HttpsURLConnection. With a pin, only the certificate
 * whose SHA-256 matches is trusted ([PinnedTrustManager]) and anything else fails the call; without one, the system's
 * certificate authorities decide. Redirects are never followed. Requests carry the bearer token and the sealed bytes,
 * nothing else: no cookies, no identifying headers.
 */
object RelayNetwork {
    private const val CONNECT_TIMEOUT_MS = 10_000
    private const val READ_TIMEOUT_MS = 30_000

    /** The relay pings every 25 seconds; a silent stream for this long is dead and is reconnected. */
    private const val STREAM_READ_TIMEOUT_MS = 70_000

    // The relay refuses more than 4 MiB; the list is a few hundred bytes per file, 64 files.
    private const val MAX_FILE_BYTES = 4 * 1024 * 1024
    private const val MAX_LIST_BYTES = 1024 * 1024

    // A pinned certificate is the identity, so its host name doesn't have to match (a LAN address, a VPN name).
    private val anyHost = HostnameVerifier { _, _ -> true }

    private val fileName = Regex("[0-9a-f]{12}\\.absync")

    /** A sync file's name as the relay and the PCs spell it: 12 lower-case hex characters and ".absync". */
    fun isFileName(name: String): Boolean = fileName.matches(name)

    private fun requireFileName(name: String) {
        if (!isFileName(name)) {
            throw RelayException(RelayProblem.Refused, "That isn't a sync file name.")
        }
    }

    /** The relay's files. A group nobody has written to yet is an empty list. */
    fun list(endpoint: RelayEndpoint): List<RelayFileInfo> {
        val connection = open(endpoint, "GET", "files")
        try {
            val code = send(connection)
            if (code == 404) {
                return emptyList()
            }

            ensureOk(code)
            val body = readAll(connection.inputStream, MAX_LIST_BYTES)
            val array = try {
                Json.parseToJsonElement(String(body, Charsets.UTF_8)) as? JsonArray
            } catch (e: Exception) {
                null
            } ?: throw RelayException(RelayProblem.Server, "The relay's file list isn't readable.")
            return array.map {
                val item = it.jsonObject
                RelayFileInfo(item.getValue("name").jsonPrimitive.content, item.getValue("etag").jsonPrimitive.content)
            }
        } finally {
            connection.disconnect()
        }
    }

    /** One file's bytes with its etag; null when the relay no longer has it. */
    fun get(endpoint: RelayEndpoint, name: String): RelayFile? {
        requireFileName(name)
        val connection = open(endpoint, "GET", "files/$name")
        try {
            val code = send(connection)
            if (code == 404) {
                return null
            }

            ensureOk(code)
            val bytes = readAll(connection.inputStream, MAX_FILE_BYTES)
            // The header's etag is quoted; the JSON ones aren't.
            return RelayFile(bytes, connection.getHeaderField("ETag")?.trim('"'))
        } finally {
            connection.disconnect()
        }
    }

    /** Stores this device's [bytes] under [name] (the first call for a group creates it). Returns the stored etag. */
    fun put(endpoint: RelayEndpoint, name: String, bytes: ByteArray): String {
        requireFileName(name)
        val connection = open(endpoint, "PUT", "files/$name")
        try {
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/octet-stream")
            connection.setFixedLengthStreamingMode(bytes.size)
            val code = send(connection) { connection.outputStream.use { it.write(bytes) } }
            ensureOk(code)
            val answer = readAll(connection.inputStream, MAX_LIST_BYTES)
            val json = try {
                Json.parseToJsonElement(String(answer, Charsets.UTF_8)) as? JsonObject
            } catch (e: Exception) {
                null
            }

            return json?.get("etag")?.jsonPrimitive?.content
                ?: throw RelayException(RelayProblem.Server, "The relay's answer isn't readable.")
        } finally {
            connection.disconnect()
        }
    }

    /** Removes [name] (this device leaving the group). A file that is already gone is fine. */
    fun delete(endpoint: RelayEndpoint, name: String) {
        requireFileName(name)
        val connection = open(endpoint, "DELETE", "files/$name")
        try {
            val code = send(connection)
            if (code != 404) {
                ensureOk(code)
            }
        } finally {
            connection.disconnect()
        }
    }

    /**
     * Opens the push stream (server-sent events) with a long read timeout. The caller reads it line by line and calls
     * disconnect to end it. A group nobody has written to yet answers 404, which is a [RelayProblem.Server] here.
     */
    fun openEvents(endpoint: RelayEndpoint): HttpsURLConnection {
        val connection = open(endpoint, "GET", "events", STREAM_READ_TIMEOUT_MS)
        try {
            connection.setRequestProperty("Accept", "text/event-stream")
            ensureOk(send(connection))
            return connection
        } catch (e: Exception) {
            connection.disconnect()
            throw e
        }
    }

    private fun open(endpoint: RelayEndpoint, method: String, path: String, readTimeoutMs: Int = READ_TIMEOUT_MS): HttpsURLConnection {
        if (!endpoint.baseUrl.startsWith("https://")) {
            throw RelayException(RelayProblem.Address, "The relay's address must start with https://.")
        }

        val connection = try {
            URL("${endpoint.baseUrl}/v1/groups/${endpoint.groupId}/$path").openConnection() as HttpsURLConnection
        } catch (e: Exception) {
            throw RelayException(RelayProblem.Address, "The relay's address isn't valid.")
        }

        connection.requestMethod = method
        connection.connectTimeout = CONNECT_TIMEOUT_MS
        connection.readTimeout = readTimeoutMs
        connection.useCaches = false
        connection.instanceFollowRedirects = false
        endpoint.pin?.let {
            connection.sslSocketFactory = PinnedTrustManager(it).socketFactory()
            connection.hostnameVerifier = anyHost
        }

        connection.setRequestProperty("Authorization", "Bearer ${endpoint.token}")
        return connection
    }

    // The status code, with a failed secure connection told apart from an unreachable relay.
    private fun send(connection: HttpsURLConnection, writeBody: (() -> Unit)? = null): Int {
        try {
            writeBody?.invoke()
            return connection.responseCode
        } catch (e: SSLException) {
            throw RelayException(RelayProblem.Certificate, "The relay's certificate isn't trusted.")
        } catch (e: IOException) {
            throw RelayException(RelayProblem.Unreachable, "Can't reach the relay.")
        }
    }

    private fun ensureOk(code: Int) {
        when (code) {
            in 200..299 -> Unit
            401 -> throw RelayException(RelayProblem.Unauthorized, "The relay refused this phone's sign-in.")
            403 -> throw RelayException(RelayProblem.OtherGroup, "The relay's group belongs to another code.")
            429 -> throw RelayException(RelayProblem.Busy, "The relay is busy.")
            400, 413 -> throw RelayException(RelayProblem.Refused, "The relay refused the request ($code).")
            else -> throw RelayException(RelayProblem.Server, "The relay answered $code.")
        }
    }

    private fun readAll(input: InputStream, limit: Int): ByteArray = input.use {
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(8192)
        while (true) {
            val count = it.read(buffer)
            if (count < 0) break
            out.write(buffer, 0, count)
            if (out.size() > limit) throw RelayException(RelayProblem.Server, "The relay's answer is too big.")
        }

        out.toByteArray()
    }
}
