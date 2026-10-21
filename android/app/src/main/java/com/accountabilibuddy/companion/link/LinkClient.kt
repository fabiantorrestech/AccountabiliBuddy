package com.accountabilibuddy.companion.link

import android.os.SystemClock
import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.data.ProfileDocumentParser
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import javax.net.ssl.SSLSocket

/**
 * The connection to the paired PC (ADR 14): TLS that trusts only the PC's pinned certificate, sign-in with the sealed
 * device token, then the PC's session as it changes. Opened while the app is on screen and closed when it isn't.
 * One request at a time (start, end early, unpair); its answer is matched by type, since state updates can arrive
 * in between.
 */
class LinkClient(
    private val store: LinkStore,
    private val deviceName: String,
    private val deviceKind: String,
    private val scope: CoroutineScope,
    /** Finds the PC on the network when it isn't at its remembered address. */
    private val discovery: LinkDiscovery,
    /** Applies the PC's settings (profiles, sleep, day bar, events); false when they couldn't be read. */
    private val applySettings: (content: JsonObject, pcName: String) -> Boolean,
    /** True when the phone lacks something only a full settings push brings (e.g. the plan version, ADR 19). */
    private val needsFullSettings: () -> Boolean = { false },
) {
    private val _status = MutableStateFlow<LinkStatus>(if (store.pc.value == null) LinkStatus.Unlinked else LinkStatus.Idle)
    private val requests = Mutex()

    @Volatile private var connection: Connection? = null
    @Volatile private var pending: Pending? = null
    private var readJob: Job? = null

    val status: StateFlow<LinkStatus> = _status.asStateFlow()

    /** The paired PC, or null. */
    val pairedPc: StateFlow<LinkedPc?> = store.pc

    /** Signs in to the paired PC, unless already connected or connecting. */
    fun connect() {
        val pc = store.pc.value ?: return
        if (connection != null || _status.value == LinkStatus.Connecting) {
            return
        }

        _status.value = LinkStatus.Connecting
        scope.launch(Dispatchers.IO) {
            val token = TokenVault.open(pc.sealedToken)
            if (token == null) {
                forget("This phone's key for the PC is gone. Pair again.")
                return@launch
            }

            try {
                val opened = openPc(pc)
                opened.send(LinkMessages.HELLO, buildJsonObject {
                    put("deviceId", pc.deviceId)
                    put("token", token)
                    put("deviceName", deviceName)
                    put("protocolVersion", LinkMessages.PROTOCOL_VERSION)
                    pc.settingsVersion?.takeUnless { needsFullSettings() }?.let { put("settingsVersion", it) }
                })
                adopt(opened, pc.pcName)
            } catch (e: IOException) {
                _status.value = LinkStatus.Offline("Can't reach ${pc.pcName} (${e.message ?: "no answer"}). Is the PC on, with linking on, on the same Wi-Fi?")
            }
        }
    }

    /**
     * The remembered address first; when the PC isn't there (a new address from the router, or another network
     * both are on), ask the network once and remember where it is now.
     */
    private fun openPc(pc: LinkedPc): Connection = try {
        Connection.open(pc.host, pc.port, pc.fingerprint)
    } catch (e: IOException) {
        val found = discovery.find(pc.fingerprint, pc.port) ?: throw e
        val moved = Connection.open(found.host, found.port, pc.fingerprint)
        if (found.host != pc.host || found.port != pc.port) {
            store.save(pc.copy(host = found.host, port = found.port))
        }

        moved
    }

    /** Closes the connection (the app left the screen). */
    fun disconnect() {
        connection?.close()
        connection = null
        readJob?.cancel()
        readJob = null
        if (store.pc.value != null) {
            _status.value = LinkStatus.Idle
        }
    }

    /** Pairs with the PC from its QR link; stays connected afterwards. Null when paired, otherwise why not. */
    suspend fun pair(link: PairingLink): String? = withContext(Dispatchers.IO) {
        disconnect()
        try {
            val opened = try {
                Connection.open(link.host, link.port, link.fingerprint)
            } catch (e: IOException) {
                // The QR code's address may be one the phone can't reach (another adapter); ask the network.
                val found = discovery.find(link.fingerprint, link.port) ?: throw e
                Connection.open(found.host, found.port, link.fingerprint)
            }
            opened.send(LinkMessages.PAIR, buildJsonObject {
                put("code", link.code)
                put("deviceName", deviceName)
                put("deviceKind", deviceKind)
            })
            val answer = opened.read() ?: return@withContext "The PC closed the connection."
            val body = answer.body as? JsonObject
            if (answer.type != LinkMessages.PAIRED || body == null) {
                opened.close()
                return@withContext body?.string("message") ?: "The PC refused to pair."
            }

            val pcName = body.string("pcName") ?: link.pcName
            store.save(
                LinkedPc(pcName, link.host, link.port, link.fingerprint, body.string("deviceId")!!, TokenVault.seal(body.string("token")!!)),
            )
            adopt(opened, pcName)
            null
        } catch (e: IOException) {
            "Couldn't reach the PC (${e.message ?: "no answer"}). Same Wi-Fi as the PC?"
        }
    }

    /** Starts a session on the PC: a profile for some minutes, or a count-up. Null when started, otherwise why not. */
    suspend fun start(profileId: String?, minutes: Int?, countUp: Boolean): String? {
        val answer = request(LinkMessages.START, buildJsonObject {
            profileId?.let { put("profileId", it) }
            minutes?.let { put("minutes", it) }
            put("countUp", countUp)
        }, LinkMessages.RESULT) ?: return "The PC didn't answer."
        return resultError(answer)
    }

    /** Asks to end early: the PC's wait and reason length, or why not. */
    suspend fun beginEndEarly(): Result<EndEarlyTerms> {
        val answer = request(LinkMessages.END_EARLY_BEGIN, null, LinkMessages.END_EARLY_TERMS, LinkMessages.RESULT)
            ?: return Result.failure(IOException("The PC didn't answer."))
        val body = answer.body as? JsonObject ?: return Result.failure(IOException("The PC sent nothing."))
        if (answer.type != LinkMessages.END_EARLY_TERMS) {
            return Result.failure(IOException(body.string("message") ?: "Can't end it now."))
        }

        return Result.success(
            EndEarlyTerms(
                waitSeconds = body.int("waitSeconds") ?: 0,
                minReasonLength = body.int("minReasonLength") ?: 0,
                note = body.string("note"),
                receivedAtElapsed = SystemClock.elapsedRealtime(),
            ),
        )
    }

    /** Ends the PC's session after the wait, with the reason. Null when ended, otherwise why not. */
    suspend fun endEarly(reason: String): String? {
        val answer = request(LinkMessages.END_EARLY, buildJsonObject { put("reason", reason) }, LinkMessages.RESULT)
            ?: return "The PC didn't answer."
        return resultError(answer)
    }

    /**
     * Sends a day plan edited on the phone (ADR 19): the plan in the PC's shape, the PC version it was edited from and
     * when. Null when the PC couldn't be asked (not connected, no answer); a PC too old for it answers UNSUPPORTED.
     */
    suspend fun sendDayPlanEdit(baseVersion: String, plan: JsonObject, editedAt: java.time.Instant): DayPlanAnswer? {
        val answer = request(LinkMessages.DAY_PLAN_EDIT, buildJsonObject {
            put("baseVersion", baseVersion)
            put("dayPlan", plan)
            put("editedAt", editedAt.toString())
        }, LinkMessages.DAY_PLAN_ANSWER, LinkMessages.RESULT) ?: return null
        val body = answer.body as? JsonObject ?: return null
        if (answer.type == LinkMessages.RESULT) {
            return DayPlanAnswer(DayPlanAnswer.UNSUPPORTED, body.string("message") ?: "The PC can't take plan changes.")
        }

        return DayPlanAnswer(
            status = body.string("status") ?: DayPlanAnswer.INVALID,
            message = body.string("message") ?: "",
            currentVersion = body.string("currentVersion"),
            currentPlan = (body["currentDayPlan"] as? JsonObject)?.let { runCatching { ProfileDocumentParser.dayPlan(it) }.getOrNull() },
            currentEditedAt = body.string("currentEditedAt")?.let { runCatching { java.time.OffsetDateTime.parse(it).toInstant() }.getOrNull() },
        )
    }

    /** Forgets the PC here and asks it to forget this phone. */
    suspend fun unpair() {
        withContext(Dispatchers.IO) {
            runCatching { connection?.send(LinkMessages.UNPAIR, null) }
        }
        forget(null)
    }

    private fun resultError(answer: LinkEnvelope): String? {
        val body = answer.body as? JsonObject ?: return "The PC sent nothing."
        return if (body.bool("ok", false)) null else body.string("message") ?: "It didn't work."
    }

    /** Sends one request and waits for the first message of one of [answers]. */
    private suspend fun request(type: String, body: JsonObject?, vararg answers: String): LinkEnvelope? = requests.withLock {
        val open = connection ?: return@withLock null
        val waiting = Pending(answers.toSet(), CompletableDeferred())
        pending = waiting
        try {
            withContext(Dispatchers.IO) { open.send(type, body) }
            withTimeoutOrNull(REQUEST_TIMEOUT_MS) { waiting.answer.await() }
        } catch (e: IOException) {
            null
        } finally {
            pending = null
        }
    }

    /** Takes over an open, signed-in (or just paired) connection and reads from it until it closes. */
    private fun adopt(opened: Connection, pcName: String) {
        connection = opened
        _status.value = LinkStatus.Connected(pcName, null)
        readJob = scope.launch(Dispatchers.IO) {
            try {
                while (true) {
                    val message = opened.read() ?: break
                    handle(message, pcName)
                }
            } catch (e: IOException) {
                // Dropped (Wi-Fi changed, the PC went to sleep or turned linking off).
            }

            if (connection === opened) {
                connection = null
                if (store.pc.value != null && _status.value !is LinkStatus.Unlinked) {
                    _status.value = LinkStatus.Offline("Lost the connection to $pcName.")
                }
            }
        }
    }

    private fun handle(message: LinkEnvelope, pcName: String) {
        val body = message.body as? JsonObject
        when (message.type) {
            LinkMessages.STATE -> body?.let { _status.value = LinkStatus.Connected(pcName, PcState.parse(it)) }
            LinkMessages.SETTINGS -> {
                val content = body?.get("content") as? JsonObject
                val version = body?.string("version")
                if (content != null && version != null && applySettings(content, pcName)) {
                    store.setSettingsVersion(version)
                }
            }
            LinkMessages.REFUSED -> forget(body?.string("message") ?: "The PC no longer knows this phone. Pair again.")
            else -> pending?.takeIf { message.type in it.types }?.answer?.complete(message)
        }
    }

    private fun forget(message: String?) {
        connection?.close()
        connection = null
        store.clear()
        _status.value = LinkStatus.Unlinked
        lastMessage.value = message
    }

    /** Why the link ended, shown once (e.g. the PC unpaired this phone). */
    val lastMessage = MutableStateFlow<String?>(null)

    private class Pending(val types: Set<String>, val answer: CompletableDeferred<LinkEnvelope>)

    /** One TLS connection; writes are serialised. */
    private class Connection(private val socket: SSLSocket) {
        private val input = DataInputStream(BufferedInputStream(socket.inputStream))
        private val output = DataOutputStream(BufferedOutputStream(socket.outputStream))

        @Synchronized
        fun send(type: String, body: JsonObject?) {
            LinkFraming.write(output, type, body)
        }

        fun read(): LinkEnvelope? = LinkFraming.read(input)

        fun close() {
            runCatching { socket.close() }
        }

        companion object {
            private const val CONNECT_TIMEOUT_MS = 5_000

            fun open(host: String, port: Int, fingerprint: String): Connection {
                val raw = Socket()
                raw.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
                val tls = PinnedTrustManager(fingerprint).socketFactory().createSocket(raw, host, port, true) as SSLSocket
                tls.startHandshake()
                return Connection(tls)
            }
        }
    }

    companion object {
        private const val REQUEST_TIMEOUT_MS = 10_000L
    }
}
