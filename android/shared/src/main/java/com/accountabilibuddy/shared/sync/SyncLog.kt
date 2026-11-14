package com.accountabilibuddy.shared.sync

import java.io.File
import java.io.IOException
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Which way a logged sync pass went. */
enum class SyncLogDirection(val text: String) {
    /** This phone put its file on a method. */
    SEND("send"),

    /** This phone read the other devices' files from a method. */
    READ("read"),

    /** A pass over the goal history files of a method. */
    HISTORY("history"),
}

/**
 * The rolling sync log (SYNC1c, ADR 86; the PC's `SyncLog`): one line per sync pass with the time, the method, the direction and counts (files,
 * parts merged, parts held, skipped, unreadable, milliseconds), device-local in a private file of the phone (never in auto-backup or a backup by
 * kind). By construction it can't hold private text: the only inputs are a method code from [SyncReceiptParts.METHODS], a [SyncLogDirection], a
 * code from [NOTES] and numbers, so never a title, note, name, file name, path, address, device name or id. Passes that changed nothing are folded
 * into one "quiet" line per method, direction and hour; lines are buffered and written together (CPU and battery: at most every [FLUSH_AFTER]);
 * only the last 7 days stay and the file never grows past [MAX_BYTES] (oldest lines go first). Thread-safe.
 */
class SyncLog(private val file: File, private val now: () -> OffsetDateTime) {
    private class QuietRun(val hour: OffsetDateTime) {
        var passes = 0
        var files = 0
        var milliseconds = 0
    }

    private val buffer = ArrayList<String>()
    private val quiet = LinkedHashMap<Pair<String, SyncLogDirection>, QuietRun>()
    private val lastLunch = HashMap<Triple<String, SyncLogDirection, String>, SyncLunch>()
    private var lastFlush: OffsetDateTime? = null
    private var lastTrim: OffsetDateTime? = null

    /**
     * One pass. A read or history pass that merged nothing, held nothing, skipped nothing and refused no part and found nothing unreadable is "quiet": it is
     * counted into the hour's one line instead of getting its own.
     */
    @Synchronized
    fun pass(method: String, direction: SyncLogDirection, files: Int, merged: Int, held: Int, skipped: Int, unreadable: Int, milliseconds: Int, refused: Int = 0) {
        if (method !in SyncReceiptParts.METHODS) return
        val at = now()
        closeQuietRunsBefore(hour(at))
        if (merged == 0 && held == 0 && unreadable == 0 && skipped == 0 && refused == 0 && direction != SyncLogDirection.SEND) {
            val run = quiet.getOrPut(method to direction) { QuietRun(hour(at)) }
            run.passes++
            run.files += files
            run.milliseconds += milliseconds
            flushIfDue(at)
            return
        }

        add(at, "$method ${direction.text} files=$files merged=$merged held=$held skipped=$skipped unreadable=$unreadable refused=$refused ms=$milliseconds")
    }

    /**
     * A lunch in a file written or read (LU1-fix; the PC's `SyncLog.Lunch`): the method, the direction and the lunch's start, end and edit stamp as UTC
     * times, nothing else. The same lunch from the same [sender] on the same method and direction is logged once (the sender only keys that and is
     * never written), so the next "late" report shows as write lag or read lag.
     */
    @Synchronized
    fun lunch(method: String, direction: SyncLogDirection, sender: String, lunch: SyncLunch) {
        if (method !in SyncReceiptParts.METHODS) return
        val key = Triple(method, direction, sender)
        if (lastLunch[key] == lunch) return
        lastLunch[key] = lunch
        val at = now()
        val edited = lunch.editedAt?.let { stamp(it.atOffset(ZoneOffset.UTC)) } ?: "none"
        add(at, "$method ${direction.text} lunch start=${stamp(lunch.start.atOffset(ZoneOffset.UTC))} end=${stamp(lunch.end.atOffset(ZoneOffset.UTC))} edited=$edited")
    }

    /** A fixed note (a code from [NOTES]), never text of any kind. */
    @Synchronized
    fun note(code: String) {
        if (code !in NOTES) return
        add(now(), "note $code")
    }

    /** Writes what is buffered now; [everything] also closes the current hour's quiet lines (a report). */
    @Synchronized
    fun flush(everything: Boolean = false) {
        closeQuietRunsBefore(if (everything) OffsetDateTime.MAX else hour(now()))
        if (buffer.isEmpty()) return
        val text = buffer.joinToString("\n", postfix = "\n")
        buffer.clear()
        lastFlush = now()
        try {
            file.parentFile?.mkdirs()
            file.appendText(text, Charsets.UTF_8)
            trim()
        } catch (e: IOException) {
            // The log is a help, never a reason to stop.
        }
    }

    /** Every line kept (the file and what is still buffered), oldest first. */
    @Synchronized
    fun lines(): List<String> {
        flush(everything = true)
        return try {
            if (file.exists()) file.readLines(Charsets.UTF_8) else emptyList()
        } catch (e: IOException) {
            emptyList()
        }
    }

    /** Forgets everything (left the group). */
    @Synchronized
    fun clear() {
        buffer.clear()
        quiet.clear()
        lastLunch.clear()
        runCatching { file.delete() }
    }

    // A quiet run belongs to its hour: once that hour is over its one line is made.
    private fun closeQuietRunsBefore(limit: OffsetDateTime) {
        for ((key, run) in quiet.entries.filter { it.value.hour.isBefore(limit) }) {
            quiet.remove(key)
            buffer.add("${stamp(run.hour)} ${key.first} ${key.second.text} quiet passes=${run.passes} files=${run.files} ms=${run.milliseconds}")
        }
    }

    private fun add(at: OffsetDateTime, text: String) {
        buffer.add("${stamp(at)} $text")
        flushIfDue(at)
    }

    private fun flushIfDue(at: OffsetDateTime) {
        val last = lastFlush
        if (last == null || Duration.between(last, at) >= FLUSH_AFTER) flush()
    }

    private fun trim() {
        val at = now()
        val trimmed = lastTrim
        if (file.length() <= MAX_BYTES && trimmed != null && Duration.between(trimmed, at) < TRIM_EVERY) return
        lastTrim = at
        val lines = file.readLines(Charsets.UTF_8).filter { line -> timeOf(line)?.let { Duration.between(it, at) <= KEEP } == true }.toMutableList()
        // Down to 90% of the cap, so the next appends do not trim again at once.
        while (lines.isNotEmpty() && lines.sumOf { it.toByteArray(Charsets.UTF_8).size + 1L } > MAX_BYTES / 10 * 9) {
            // Oldest first, a tenth at a time.
            repeat(maxOf(1, lines.size / 10)) { lines.removeAt(0) }
        }

        val temporary = File(file.parentFile, file.name + ".tmp")
        temporary.writeText(if (lines.isEmpty()) "" else lines.joinToString("\n", postfix = "\n"), Charsets.UTF_8)
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    private fun timeOf(line: String): OffsetDateTime? {
        val space = line.indexOf(' ')
        if (space <= 0) return null
        return runCatching { OffsetDateTime.parse(line.substring(0, space)) }.getOrNull()
    }

    private fun stamp(time: OffsetDateTime): String = STAMP.format(time.withOffsetSameInstant(ZoneOffset.UTC))

    private fun hour(time: OffsetDateTime): OffsetDateTime = time.withOffsetSameInstant(ZoneOffset.UTC).withMinute(0).withSecond(0).withNano(0)

    companion object {
        /** How long a line is kept. */
        val KEEP: Duration = Duration.ofDays(7)

        /** The most the file holds (512 KB). */
        const val MAX_BYTES = 512L * 1024

        /** The fixed notes: a re-read, a send again, a history re-read, a history send again, a stale file removed, a file ignored, a file tried again. */
        val NOTES: List<String> = listOf("reread", "resend", "history-reread", "history-resend", "stale-removed", "ignored", "retry")

        private val FLUSH_AFTER: Duration = Duration.ofSeconds(10)
        private val TRIM_EVERY: Duration = Duration.ofHours(6)
        private val STAMP: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)
    }
}
