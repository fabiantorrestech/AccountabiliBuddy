package com.accountabilibuddy.shared.sync

/** What one "Re-read everything" over a method did (SYNC1c; counts only). [requests]: the requests made to the method; [stopped]: ended early (a 429). */
data class SyncRereadResult(val method: String, val files: Int, val problems: Int, val requests: Int, val stopped: Boolean) {
    companion object {
        /** Nothing done (the method is off or the phone is in no group). */
        fun none(method: String) = SyncRereadResult(method, 0, 0, 0, false)
    }
}

/** A file of the relay's listing, by name: its [size] and [etag]. */
data class RelayRereadFile(val name: String, val size: Long, val etag: String)

/** What the relay's get gave back: the [bytes] and the file's [etag] (null: the listing's). */
class RelayRereadDownload(val bytes: ByteArray, val etag: String?)

/** What a relay re-read needs from the relay and the phone: listing, a download and taking a file in. Each call is one request, except [accept]. */
interface RelayRereadPort {
    /** One request: the relay's files that are sync file names of other devices (goal history names are left to their own pass). */
    fun list(): List<RelayRereadFile>

    /** One request; null when the file is gone. Throws [RelayBusyException] on a 429 and [java.io.IOException] on any other failure. */
    fun get(name: String): RelayRereadDownload?

    /** Whether the downloaded bytes are a goal history file (their own pass reads them: noted, never merged here). */
    fun isHistoryFile(bytes: ByteArray): Boolean

    /** Takes the file in (the same path a normal read uses). True when it is a problem (could not be read). */
    fun accept(name: String, download: RelayRereadDownload): Boolean

    /** Whether the group is still the one the pass started in (a rekey switches it). */
    fun sameGroup(): Boolean
}

/** The relay answered 429 (too many requests): a re-read ends at once. */
class RelayBusyException : RuntimeException("The relay is busy.")

/**
 * "Re-read everything" on the relay (SYNC1c; the PC's `RereadAllAsync`): one listing and one download per sync file, every request spread by the
 * [RelayRequestPacer] to at most 30 a minute (a PC and a phone on one home network share the relay's 120 a minute, and a goal history pass may take
 * up to 40), ended at once by a 429 answer (never retried: the next run is five minutes away) and capped at [MAX_PASS_BYTES] of downloads. Merging
 * is idempotent: nothing changes that was already merged. Never run alongside another pass: the caller holds the relay's gate.
 */
object RelayRereader {
    /** What one pass downloads at most (the same cap a normal read has). */
    const val MAX_PASS_BYTES = 32L * 1024 * 1024

    /** A sync file bigger than this is not downloaded (a problem). */
    const val MAX_FILE_BYTES = 4L * 1024 * 1024

    fun run(method: String, port: RelayRereadPort, pacer: RelayRequestPacer): SyncRereadResult {
        val listed = try {
            pacer.await()
            port.list()
        } catch (e: RelayBusyException) {
            return SyncRereadResult(method, 0, 1, pacer.started, true)
        } catch (e: java.io.IOException) {
            return SyncRereadResult(method, 0, 1, pacer.started, false)
        }

        var files = 0
        var problems = 0
        var stopped = false
        var passBytes = 0L
        for (info in listed) {
            if (!port.sameGroup() || passBytes >= MAX_PASS_BYTES) break
            if (info.size > MAX_FILE_BYTES) {
                problems++
                continue
            }

            try {
                pacer.await()
                val download = port.get(info.name) ?: continue
                passBytes += download.bytes.size
                if (port.isHistoryFile(download.bytes)) continue
                files++
                if (port.accept(info.name, download)) problems++
            } catch (e: RelayBusyException) {
                // The relay's answer to too many requests: this run ends here (no retry).
                problems++
                stopped = true
                break
            } catch (e: java.io.IOException) {
                problems++
            }
        }

        return SyncRereadResult(method, files, problems, pacer.started, stopped)
    }
}
