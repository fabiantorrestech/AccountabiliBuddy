package com.accountabilibuddy.companion.session

import com.accountabilibuddy.shared.session.PhoneSessionCodec
import com.accountabilibuddy.shared.session.PhoneSessionRead
import com.accountabilibuddy.shared.session.PhoneSessionReason
import com.accountabilibuddy.shared.session.PhoneSessionStoreData
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.time.Instant

/**
 * The phone session's two sealed files (ADR 85, plan section 2): `phone-sessions.sealed` (the running checkpoint and the 90-day log) and
 * `phone-session-reasons.sealed` (end reasons, 90 days). Neither is synced, backed up or logged. No Android type: the sealing is handed in
 * ([PhoneSessionSealer]), so the checks run it on the JVM.
 *
 * "Can't open" is not "empty" (B5): [read] says [PhoneSessionRead.Unreadable] for a file that exists but can't be opened or read, so the
 * caller never writes a file that tells receivers the phone has no history.
 */
class PhoneSessionStore(private val sessionsFile: File, private val reasonsFile: File, private val sealer: PhoneSessionSealer) {
    @Synchronized
    fun read(): PhoneSessionRead {
        if (!sessionsFile.exists()) return PhoneSessionRead.Missing
        val data = runCatching { openWithRetry(sessionsFile.readText())?.let(PhoneSessionCodec::read) }.getOrNull()
        return if (data == null) PhoneSessionRead.Unreadable else PhoneSessionRead.Readable(data)
    }

    /** Saves [data] in one atomic replace; false when it could not be kept (the old file stays). */
    @Synchronized
    fun write(data: PhoneSessionStoreData): Boolean = writeSealed(sessionsFile, PhoneSessionCodec.write(data))

    /**
     * Keeps a copy of an unreadable file next to it under a name no earlier copy has (`.unreadable`, `.unreadable.1`, ...): an earlier copy is never
     * overwritten, and the file that is unreadable NOW is always the one copied (S5). True when this file was copied.
     */
    @Synchronized
    fun setAsideUnreadable(): Boolean = runCatching {
        var aside = File(sessionsFile.parentFile, sessionsFile.name + UNREADABLE_SUFFIX)
        var n = 0
        while (aside.exists()) aside = File(sessionsFile.parentFile, sessionsFile.name + UNREADABLE_SUFFIX + "." + (++n))
        sessionsFile.copyTo(aside)
        aside.exists()
    }.getOrDefault(false)

    /** The kept reasons, newest last; an unreadable file reads as none (a reason stays on this phone only). */
    @Synchronized
    fun readReasons(): List<PhoneSessionReason> {
        if (!reasonsFile.exists()) return emptyList()
        return runCatching { openWithRetry(reasonsFile.readText())?.let(PhoneSessionCodec::readReasons) }.getOrNull().orEmpty()
    }

    /**
     * Keeps [reason] for [KEPT_DAYS] days; the older ones are dropped only when [prune] (a trusted [now]: an unchecked clock set far ahead must not sweep them, S6).
     * False when it could not be kept.
     */
    @Synchronized
    fun addReason(reason: PhoneSessionReason, now: Instant, prune: Boolean): Boolean {
        val limit = now.minusSeconds(KEPT_DAYS * 24 * 3600)
        val kept = readReasons().let { all -> if (prune) all.filter { it.at.isAfter(limit) } else all } + reason
        return writeSealed(reasonsFile, PhoneSessionCodec.writeReasons(kept))
    }

    private fun writeSealed(file: File, text: String): Boolean = runCatching {
        file.parentFile?.mkdirs()
        val temp = File(file.parentFile, file.name + ".tmp")
        // Written and synced to the disk BEFORE the rename, so a power cut right after a write can't leave an empty or half-written file where the running
        // session was (S5; the AtomicFile pattern).
        java.io.FileOutputStream(temp).use { out ->
            out.write(sealer.seal(text).toByteArray(Charsets.UTF_8))
            out.fd.sync()
        }
        try {
            Files.move(temp.toPath(), file.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
        } catch (ignored: java.nio.file.AtomicMoveNotSupportedException) {
            Files.move(temp.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING)
        }
        true
    }.getOrDefault(false)

    // A Keystore that errs for a moment looks like a file that can't be opened: try again before calling it unreadable (S5).
    private fun openWithRetry(sealed: String): String? {
        repeat(OPEN_TRIES) { sealer.open(sealed)?.let { return it } }
        return null
    }

    companion object {
        const val OPEN_TRIES = 3
        const val SESSIONS_FILE = "phone-sessions.sealed"
        const val REASONS_FILE = "phone-session-reasons.sealed"
        const val UNREADABLE_SUFFIX = ".unreadable"
        const val KEPT_DAYS = 90L
    }
}
