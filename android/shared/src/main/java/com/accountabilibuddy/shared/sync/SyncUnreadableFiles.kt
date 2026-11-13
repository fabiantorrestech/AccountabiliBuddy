package com.accountabilibuddy.shared.sync

import java.security.MessageDigest
import java.time.OffsetDateTime
import java.util.UUID

/**
 * One file a sync method holds that this phone does not take in (SYNC1c, the PC's `SyncUnreadableFile`): why, since when, and whose it is when
 * that can be told. The real [name] is kept only so "Try again" and "Remove my stale file" can name the file to the method; it is never shown,
 * logged or exported (the screen shows the device's ordinal).
 *
 * @property reason One of [SyncReceiptParts.FAILURES], [OTHER_GROUP] or [OWN_COPY].
 * @property device The device the file's name belongs to (by `fileNameFor` over the known devices); null when unknown.
 * @property ownStale The file is this phone's own: an old-group name or an extra copy of its current file (the only files it may remove).
 * @property ignored Hidden by "Ignore" (a line only: nothing else changes).
 */
data class SyncUnreadableFile(
    val method: String,
    val key: String,
    val name: String,
    val reason: String,
    val at: OffsetDateTime,
    val device: UUID?,
    val ownStale: Boolean,
    val ignored: Boolean,
) {
    /** Whether this entry is a problem (not an old-group leftover and not this phone's own extra copy). */
    val isProblem: Boolean get() = reason != OTHER_GROUP && reason != OWN_COPY

    companion object {
        /** A leftover of another group (before a device removal): information, not a problem. */
        const val OTHER_GROUP = "otherGroup"

        /** An extra copy of this phone's own file (a Drive duplicate, a conflict copy in the folder): stale, never a problem. */
        const val OWN_COPY = "ownCopy"
    }
}

/**
 * The files the sync methods hold that this phone does not take in, by method and file (the PC's `SyncUnreadableFiles`): in memory only (every
 * start reads everything again). The sources report what a pass found and clear a file once it reads or is gone; the troubleshooting section lists
 * it. "Ignore" only hides a line; it never feeds a method's problem line and never changes what a source does.
 */
class SyncUnreadableFiles(private val now: () -> OffsetDateTime) {
    private val files = LinkedHashMap<Pair<String, String>, SyncUnreadableFile>()

    /** Called (on any thread) after the list changed. */
    @Volatile
    var onChanged: () -> Unit = {}

    /** Every entry, newest first. */
    @Synchronized
    fun all(): List<SyncUnreadableFile> = files.values.sortedWith(compareByDescending<SyncUnreadableFile> { it.at.toInstant() }.thenBy { it.key })

    /** The entries shown: not ignored. */
    fun shown(): List<SyncUnreadableFile> = all().filter { !it.ignored }

    /** A file did not read (or is a leftover): recorded, keeping an earlier "Ignore" of the same file with the same reason. */
    fun report(method: String, name: String, reason: String, device: UUID?, ownStale: Boolean) {
        val changed = synchronized(this) {
            val existing = files[method to name]
            val sameReason = existing != null && existing.reason == reason
            val entry = SyncUnreadableFile(
                method, keyOf(method, name), name, reason, if (sameReason) existing!!.at else now(), device, ownStale, sameReason && existing!!.ignored,
            )
            if (existing == entry) {
                false
            } else {
                files[method to name] = entry
                true
            }
        }

        if (changed) onChanged()
    }

    /** The file reads now (or its name is no longer a problem): it leaves the list. */
    fun clear(method: String, name: String) {
        if (synchronized(this) { files.remove(method to name) != null }) onChanged()
    }

    /** Drops the method's entries whose file is no longer there (a full listing of the method). */
    fun prune(method: String, existingNames: Set<String>) {
        val changed = synchronized(this) {
            val gone = files.keys.filter { it.first == method && it.second !in existingNames }
            gone.forEach { files.remove(it) }
            gone.isNotEmpty()
        }

        if (changed) onChanged()
    }

    /** Drops every entry (the group changed or the method was switched off); [method] limits it to one method. */
    fun clearAll(method: String? = null) {
        val changed = synchronized(this) {
            val before = files.size
            files.keys.filter { method == null || it.first == method }.forEach { files.remove(it) }
            files.size != before
        }

        if (changed) onChanged()
    }

    /** Hides one line. The file, its method and every other setting stay as they are. */
    fun ignore(method: String, key: String) {
        val changed = synchronized(this) {
            val pair = files.entries.firstOrNull { it.key.first == method && it.value.key == key }
            if (pair == null || pair.value.ignored) {
                false
            } else {
                files[pair.key] = pair.value.copy(ignored = true)
                true
            }
        }

        if (changed) onChanged()
    }

    /** The entry with this key on this method; null when it is gone. */
    @Synchronized
    fun find(method: String, key: String): SyncUnreadableFile? = files.values.firstOrNull { it.method == method && it.key == key }

    private fun keyOf(method: String, name: String): String =
        MessageDigest.getInstance("SHA-256").digest("$method|$name".toByteArray(Charsets.UTF_8)).take(4).joinToString("") { "%02x".format(it) }
}
