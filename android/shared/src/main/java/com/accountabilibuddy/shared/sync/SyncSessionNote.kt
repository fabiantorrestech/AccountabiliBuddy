package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A session note (ADR 87; the PC's `SyncSessionNote`) as the sealed sync files carry it in [SyncFile.sessionNotes], merged item by item by
 * [SharedListMerge]: a line the user typed in Focus mode. Private text: only inside sealed files between the user's own devices, never in
 * a QR file, the watch's snapshot, a log or the copy kept of another device's file. A deleted note travels as a tombstone with no text
 * (kept 30 days). Session reasons never travel.
 *
 * @property text The note; null on a tombstone.
 * @property at When it was written; the 90-day window counts from it.
 * @property sessionStart The start of the session it was written in (that device's own session); null when not matched.
 * @property profileId The focus profile's id of that session (an id, never a window title, app or URL).
 * @property by The device that wrote it (an id only): a session review shows only its own device's notes.
 */
data class SyncSessionNote(
    override val id: String,
    val text: String?,
    val at: Instant,
    val sessionStart: Instant?,
    val profileId: String?,
    val by: String,
    override val editedAt: Instant,
    override val deletedAt: Instant? = null,
) : SharedItem {
    /** Oldest first, as the notes are written (the PC's `At.ToUnixTimeMilliseconds()`). */
    override val position: Double get() = at.toEpochMilli().toDouble()

    override val tieBreak: String
        get() = listOf(text ?: "", at.ticks().toString(), sessionStart?.ticks()?.toString() ?: "", profileId?.lowercase() ?: "", by.lowercase()).joinToString("\u0001")

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncSessionNote = copy(editedAt = editedAt, deletedAt = deletedAt)

    /** The note as a tombstone: its text and session are gone, and it is deleted at [at]. */
    fun deleted(at: Instant): SyncSessionNote = copy(text = null, sessionStart = null, profileId = null, editedAt = at, deletedAt = at)
}
