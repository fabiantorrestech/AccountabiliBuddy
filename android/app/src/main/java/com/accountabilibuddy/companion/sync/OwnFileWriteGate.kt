package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant

/**
 * Decides whether this phone's own sync file needs writing to one method (folder, Drive or relay). Every rewrite makes
 * a new remote version, and a folder carried by Drive for desktop + Autosync turns each one into a "(conflict ...)"
 * copy, so the file is written only when its content changed (compared before sealing: the sealed bytes always differ,
 * the nonce is random; the write time is left out of the comparison), plus a slow heartbeat so the other devices don't
 * think the phone is gone. During a PC focus session the heartbeat is short, because the PC reads the phone's
 * placement from it (phase 11b).
 */
class OwnFileWriteGate {
    private var lastHash: String? = null
    private var lastWriteAt: Instant? = null
    private var pendingHash: String? = null

    /** True when [file] should be written now; call [written] after it was. */
    @Synchronized
    fun due(file: SyncFile, now: Instant, inFocusSession: Boolean): Boolean {
        val hash = contentHash(file)
        pendingHash = hash
        val last = lastWriteAt ?: return true
        val heartbeat = if (inFocusSession) SESSION_HEARTBEAT else IDLE_HEARTBEAT
        return hash != lastHash || Duration.between(last, now) >= heartbeat
    }

    /** The own file was written (the content [due] looked at, at [now]). */
    @Synchronized
    fun written(now: Instant) {
        lastHash = pendingHash
        lastWriteAt = now
    }

    /** Forget what was written, so the next sync writes the file (switched off, signed out, group left). */
    @Synchronized
    fun reset() {
        lastHash = null
        lastWriteAt = null
        pendingHash = null
    }

    private fun contentHash(file: SyncFile): String {
        val stable = file.copy(device = file.device.copy(writtenAt = Instant.EPOCH))
        val digest = MessageDigest.getInstance("SHA-256").digest(SyncFileJson.write(stable).toString().toByteArray(Charsets.UTF_8))
        return digest.joinToString("") { "%02x".format(it) }
    }

    companion object {
        val IDLE_HEARTBEAT: Duration = Duration.ofMinutes(30)
        val SESSION_HEARTBEAT: Duration = Duration.ofMinutes(2)
    }
}
