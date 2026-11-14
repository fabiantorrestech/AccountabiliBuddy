package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * The rules for session notes that travel between the user's own devices (ADR 87); the PC's twin is Core's `SessionNoteRules`: the merge
 * (newest edit wins, tombstones kept 30 days, a stamp more than 2 minutes ahead is clamped, as every shared list, ADR 48), the 90-day window,
 * which notes a device's own session shows, and what a file's part is cleaned to.
 */
object SessionNoteRules {
    /** How long a note is kept (and sent), counted from when it was written: the same window as the task history (ADR 72). */
    val WINDOW: Duration = Duration.ofDays(90)

    private const val NO_DEVICE = "00000000-0000-0000-0000-000000000000"

    /** The merge of this device's notes and another's, tombstones included, then cut to the window; the same whichever is [first]. */
    fun merge(first: List<SyncSessionNote>, second: List<SyncSessionNote>, now: Instant): List<SyncSessionNote> =
        inWindow(SharedListMerge.merge(first, second, now), now)

    /** The notes as this device writes them into its file: every live note and each tombstone of the last 30 days, inside the window. */
    fun forFile(notes: List<SyncSessionNote>, now: Instant): List<SyncSessionNote> = merge(notes, emptyList(), now)

    /** Drops what is older than the window. */
    fun inWindow(notes: List<SyncSessionNote>, now: Instant): List<SyncSessionNote> {
        val from = now.minus(WINDOW)
        return notes.filter { it.at.ticks() > from.ticks() }
    }

    /**
     * The texts of this device's own notes written from [from] to [to] (a session of this device), oldest first. A note another device wrote
     * never shows in this device's session review, whatever its time; a note with no writing device yet is this device's own.
     */
    fun between(notes: List<SyncSessionNote>, self: String, from: Instant, to: Instant): List<String> = notes
        .filter {
            it.deletedAt == null && it.text != null && (it.by.equals(self, ignoreCase = true) || it.by == NO_DEVICE) &&
                it.at.ticks() >= from.ticks() && it.at.ticks() <= to.ticks()
        }
        .sortedWith(compareBy<SyncSessionNote> { it.at.ticks() }.thenBy { it.id.lowercase() })
        .map { it.text!! }

    /**
     * What a file's notes are cleaned to as it opens: an entry with no id, no writing device, no time or (when live) no text is left out
     * (leaving one out never deletes); a tombstone carries no text.
     */
    fun clean(notes: List<SyncSessionNote>): List<SyncSessionNote> = notes
        .filter {
            it.id != NO_DEVICE && it.by != NO_DEVICE && it.at.ticks() != 0L && it.editedAt.ticks() != 0L &&
                (it.deletedAt != null || !it.text.isNullOrBlank())
        }
        .map { if (it.deletedAt == null) it else it.copy(text = null, sessionStart = null, profileId = null) }
}
