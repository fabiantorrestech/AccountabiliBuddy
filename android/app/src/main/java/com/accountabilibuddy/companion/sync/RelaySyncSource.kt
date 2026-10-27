package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.RelayIdentity
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFolder
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.BufferedReader
import java.io.IOException
import java.io.InputStreamReader
import java.io.Reader
import java.time.Instant
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

/**
 * The phone's "Own relay" method (ADR 46): reads the other devices' sealed files from the user's relay (downloading a
 * file again only when its etag changed), then writes this phone's own file and the files it carries that the relay
 * doesn't have yet, under the same opaque names the PCs use. It polls at [DrivePoll]'s pace (30 seconds while a
 * session runs or the timeline is pinned, else 15 minutes). While that busy state holds it also keeps the relay's push
 * stream open, so a change on a PC arrives in about a second; the stream reconnects after 1 s, doubling to 60 s. The
 * relay is trusted only with sealed bytes: the pin (or normal certificate checks) is never skipped, and a mismatch is a
 * problem on the status line, not a fallback.
 */
class RelaySyncSource(
    private val sync: SyncCoordinator,
    private val scope: CoroutineScope,
    /** Runs before each full sync, so this phone's file says what's true now (e.g. its linked watch is still valid). */
    private val beforeSync: suspend () -> Unit = {},
) {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file on the relay as last read or written, so a carried file is written only when newer.
    private val inRelay = mutableMapOf<UUID, Instant>()

    // Each file's etag as last read or written by this phone. A push naming one of these is this phone's own echo (or
    // already read) and is ignored, so a write can't start a read-write loop. The stream thread reads it too.
    private val seen = ConcurrentHashMap<String, String>()

    // The relay and group the two maps above belong to, so another relay's or group's names never mix in.
    private var cacheOwner: String? = null

    private var soon: Job? = null

    // Writes this phone's own file only when it changed (or on the slow heartbeat).
    private val ownGate = OwnFileWriteGate()

    val status: StateFlow<FolderStatus> = _status.asStateFlow()

    /** Polls at [DrivePoll]'s pace and keeps the push stream open while [busy]; runs until cancelled. */
    suspend fun loop(busy: () -> Boolean) = coroutineScope {
        launch { pollLoop(busy) }
        launch { streamLoop(busy) }
    }

    /** Reads, then writes; does nothing while the switch is off or the phone isn't in a group. */
    suspend fun syncNow() = syncOnce(write = true)

    /** Like [syncNow] a moment later and once for a burst of calls (several files taken in at the same time). */
    @Synchronized
    fun syncSoon() {
        if (!isEnabled() || soon?.isActive == true) {
            return
        }

        soon = scope.launch {
            delay(SOON_MS)
            syncNow()
        }
    }

    /** Switched off or the group left: forget what was read, so the next time reads everything again. */
    fun forget() {
        inRelay.clear()
        seen.clear()
        ownGate.reset()
        cacheOwner = null
        _status.value = FolderStatus()
    }

    /**
     * This phone is leaving the group (call before the group's key is dropped): asks the relay to delete its file, in
     * the background and best effort, then forgets what was read.
     */
    fun leaveGroup() {
        val endpoint = endpoint()
        val ownName = sync.ownFolderFileName()
        if (isEnabled() && endpoint != null && ownName != null) {
            scope.launch(Dispatchers.IO) { runCatching { RelayNetwork.delete(endpoint, ownName) } }
        }

        forget()
    }

    private suspend fun pollLoop(busy: () -> Boolean) {
        while (true) {
            syncNow()
            // Checked again at every busy step, so a request to a PC or a session starting shortens a long wait.
            val step = DrivePoll.interval(true).toMillis()
            var waited = 0L
            while (waited < DrivePoll.interval(busy()).toMillis()) {
                delay(step)
                waited += step
            }
        }
    }

    private suspend fun streamLoop(busy: () -> Boolean) {
        var backoff = BACKOFF_START_MS
        while (true) {
            if (!shouldStream(busy)) {
                backoff = BACKOFF_START_MS
                delay(CHECK_MS)
                continue
            }

            var connected = false
            try {
                connected = stream(busy)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // The stream didn't open or broke; the polling reports the problem (certificate, sign-in, address), so the
                // status line doesn't flicker. Reconnect below.
            }

            if (!shouldStream(busy)) {
                continue
            }

            if (connected) {
                backoff = BACKOFF_START_MS
            }

            delay(backoff)
            if (!connected) {
                backoff = minOf(backoff * 2, BACKOFF_MAX_MS)
            }
        }
    }

    // Holds the push stream open until it ends or [shouldStream] turns false. True when the relay accepted the stream
    // (so a stream that later breaks reconnects after 1 s); an exception means it never opened.
    private suspend fun stream(busy: () -> Boolean): Boolean {
        val endpoint = endpoint() ?: return false
        val connection = withContext(Dispatchers.IO) { RelayNetwork.openEvents(endpoint) }
        val signature = signatureOf(endpoint)
        try {
            coroutineScope {
                val pushes = Channel<Boolean>(Channel.CONFLATED)
                // Ends the blocked read when the phone stops being busy, the switch goes off or the relay is changed.
                val watcher = launch {
                    try {
                        while (shouldStream(busy) && endpoint()?.let(::signatureOf) == signature) {
                            delay(CHECK_MS)
                        }
                    } finally {
                        connection.disconnect()
                    }
                }
                val reader = launch { for (push in pushes) if (push) readPass() }
                // What changed while the stream was closed.
                launch { readPass() }
                try {
                    withContext(Dispatchers.IO) {
                        readEvents(BufferedReader(InputStreamReader(connection.inputStream, Charsets.UTF_8)), pushes)
                    }
                } finally {
                    pushes.close()
                    watcher.cancel()
                    reader.join()
                }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // The stream ended or broke after it opened; the caller reconnects.
        }

        return true
    }

    // Blocks reading "event: changed" / "data: {name, etag}" until the stream ends. Comments (":ping") are skipped.
    private fun readEvents(input: Reader, pushes: Channel<Boolean>) {
        val ownName = sync.ownFolderFileName()
        var event: String? = null
        while (true) {
            val line = readLine(input) ?: return
            when {
                line.isEmpty() -> event = null
                line.startsWith("event:") -> event = line.substring("event:".length).trim()
                line.startsWith("data:") && event == "changed" -> {
                    if (isForeignChange(line.substring("data:".length).trim(), ownName)) {
                        pushes.trySend(true)
                    }
                }
            }
        }
    }

    // True when a push names a file this phone doesn't already have at that etag.
    private fun isForeignChange(data: String, ownName: String?): Boolean {
        val json = runCatching { Json.parseToJsonElement(data) as JsonObject }.getOrNull() ?: return false
        val name = json["name"]?.jsonPrimitive?.content ?: return false
        val etag = json["etag"]?.jsonPrimitive?.content.orEmpty()
        // No etag: the file was deleted (someone left); there is nothing to read.
        return etag.isNotEmpty() && name != ownName && seen[name] != etag
    }

    // A line of at most [MAX_LINE] characters; null at the end of the stream.
    private fun readLine(input: Reader): String? {
        val line = StringBuilder()
        while (true) {
            val character = input.read()
            if (character < 0) {
                return if (line.isEmpty()) null else line.toString()
            }

            if (character == '\n'.code) {
                return line.toString().trimEnd('\r')
            }

            if (line.length >= MAX_LINE) {
                throw IOException("A line in the relay's stream is too long.")
            }

            line.append(character.toChar())
        }
    }

    // Reads only (what a push or a reconnect asks for); this phone's own file isn't written again.
    private suspend fun readPass() = syncOnce(write = false)

    private suspend fun syncOnce(write: Boolean) {
        if (!isEnabled()) {
            return
        }

        val ownName = sync.ownFolderFileName() ?: return
        running.withLock {
            val endpoint = endpoint()
            if (endpoint == null) {
                _status.value = FolderStatus(_status.value.lastSyncAt, describe(RelayProblem.Address))
                return
            }

            if (write) {
                try {
                    beforeSync()
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    // A failed check of the watch link doesn't stop the sync.
                }
            }

            _status.value = withContext(Dispatchers.IO) {
                try {
                    if (cacheOwner != signatureOf(endpoint)) {
                        inRelay.clear()
                        seen.clear()
                        cacheOwner = signatureOf(endpoint)
                    }

                    val listed = RelayNetwork.list(endpoint)
                    val unreadable = read(endpoint, listed, ownName)
                    if (write) {
                        write(endpoint)
                    }

                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file on the relay." else null)
                } catch (e: RelayException) {
                    FolderStatus(_status.value.lastSyncAt, describe(e.problem))
                } catch (e: Exception) {
                    FolderStatus(_status.value.lastSyncAt, describe(RelayProblem.Unreachable))
                }
            }
        }
    }

    // True when a file couldn't be read (another group's, or damaged).
    private fun read(endpoint: RelayEndpoint, listed: List<RelayFileInfo>, ownName: String): Boolean {
        var unreadable = false
        var passBytes = 0L
        for (file in listed) {
            if (passBytes >= MAX_PASS_BYTES) {
                break // Enough for one pass; the rest waits for the next.
            }

            // Only the strict name the PCs write is ever fetched; the relay's list is not trusted for paths.
            if (!RelayNetwork.isFileName(file.name) || !SyncFolder.isOtherDevicesFile(file.name, ownName) || seen[file.name] == file.etag) {
                continue
            }

            val downloaded = RelayNetwork.get(endpoint, file.name) ?: continue
            passBytes += downloaded.bytes.size
            val etag = downloaded.etag ?: file.etag
            val card = sync.cardOf(downloaded.bytes)
            if (card == null) {
                // Another group's or damaged: not read again until it changes.
                seen[file.name] = etag
                unreadable = true
                continue
            }

            inRelay[card.deviceId] = maxOf(card.writtenAt, inRelay[card.deviceId] ?: Instant.MIN)
            val error = sync.acceptSealed(downloaded.bytes, SyncDeviceCard.METHOD_RELAY)
            // Remembered only once taken in, or when this phone holds a copy at least as new (nothing to gain by
            // reading it again); a refusal for any other reason is retried at the next pass.
            val held = sync.received.value.firstOrNull { it.file.device.deviceId == card.deviceId }
            if (error == null || (held != null && held.file.device.writtenAt >= card.writtenAt)) {
                seen[file.name] = etag
            }
        }

        return unreadable
    }

    private fun write(endpoint: RelayEndpoint) {
        for (file in sync.filesForFolder(inRelay, ownGate, SyncDeviceCard.METHOD_RELAY)) {
            seen[file.name] = RelayNetwork.put(endpoint, file.name, file.sealed)
            inRelay[file.deviceId] = file.writtenAt
            if (file.own) {
                ownGate.written(Instant.now())
            }
        }
    }

    private fun describe(problem: RelayProblem): String = when (problem) {
        RelayProblem.Address -> "The relay's address isn't valid (Devices)."
        RelayProblem.Unreachable -> "Can't reach the relay right now."
        RelayProblem.Certificate ->
            if (sync.settings.value.relayPin != null) "The relay's certificate doesn't match its pin. Check the address and pin (Devices)."
            else "Android doesn't trust the relay's certificate. If it is self-signed, add its pin (Devices)."
        RelayProblem.Unauthorized -> "The relay refused this phone's sign-in."
        RelayProblem.OtherGroup -> "This relay already holds a group made with a different link code."
        RelayProblem.AdmissionRequired ->
            "This relay needs its admission key to start a new group. Add it in Devices (Own relay), or join with the link from a device that has it."
        RelayProblem.Busy -> "The relay is busy; trying again later."
        RelayProblem.Refused -> "The relay refused a file (too big?)."
        RelayProblem.Server -> "The relay answered something unexpected."
    }

    private fun isEnabled(): Boolean {
        val settings = sync.settings.value
        return settings.relayOn && settings.relayUrl != null && sync.isJoined
    }

    private fun shouldStream(busy: () -> Boolean): Boolean = isEnabled() && busy()

    private fun endpoint(): RelayEndpoint? {
        val settings = sync.settings.value
        val key = sync.key.value ?: return null
        val url = settings.relayUrl ?: return null

        // The group's id and token are this relay's own (named by its pin, else its address), not the same on every relay.
        val relay = RelayIdentity.of(url, settings.relayPin) ?: return null
        return RelayEndpoint(url, settings.relayPin, key.relayGroupIdFor(relay), key.relayTokenFor(relay), settings.relayAdmission)
    }

    // Which relay and group a stream belongs to, to end it when either changes (the token follows the group).
    private fun signatureOf(endpoint: RelayEndpoint): String = "${endpoint.baseUrl}|${endpoint.pin}|${endpoint.groupId}"

    private companion object {
        const val SOON_MS = 1_000L
        const val CHECK_MS = 2_000L
        const val BACKOFF_START_MS = 1_000L
        const val BACKOFF_MAX_MS = 60_000L
        const val MAX_LINE = 4_096

        /** What one read pass downloads at most: a hostile relay can't keep this phone downloading junk over mobile data. */
        const val MAX_PASS_BYTES = 32L * 1024 * 1024
    }
}
