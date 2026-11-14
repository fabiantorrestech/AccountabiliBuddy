package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import java.time.Instant

/**
 * What this phone does to the session notes it holds (AR1c, ADR 87), pure; the PC's twin is `SessionNotesStore`. The phone types no new note
 * (that is the PCs' capture box); it shows every device's notes, edits a text and deletes one, each as a stamped change that reaches the other
 * devices through the sync file ([SessionNoteRules.forFile]). Note text is private: nothing here logs it.
 */
object SessionNoteEdits {
    /**
     * Changes a live note's text: stamped with [SharedListMerge.nextStamp] so it outranks what it changed, [SyncSessionNote.by] stays the writing
     * device. A blank text, an unknown or deleted id and an unchanged text change nothing (the same list comes back).
     */
    fun edit(notes: List<SyncSessionNote>, id: String, text: String, now: Instant): List<SyncSessionNote> {
        val trimmed = text.trim()
        val held = notes.firstOrNull { it.id.equals(id, ignoreCase = true) && it.deletedAt == null } ?: return notes
        if (trimmed.isEmpty() || held.text == trimmed) return notes
        val changed = held.copy(text = trimmed, editedAt = SharedListMerge.nextStamp(now, held))
        return notes.map { if (it === held) changed else it }
    }

    /** Deletes a live note: its text is gone at once and the tombstone tells the other devices; an unknown id changes nothing. */
    fun delete(notes: List<SyncSessionNote>, id: String, now: Instant): List<SyncSessionNote> {
        val held = notes.firstOrNull { it.id.equals(id, ignoreCase = true) && it.deletedAt == null } ?: return notes
        val gone = held.deleted(SharedListMerge.nextStamp(now, held))
        return notes.map { if (it === held) gone else it }
    }

    /**
     * Takes in the notes of another device's file or of a backup, merged item by item (an edit or a deletion made there reaches this phone, a note only
     * here stays) and cut to the window ([SessionNoteRules.merge]); the incoming entries are cleaned first (a broken one is left out, never a delete).
     */
    fun receive(held: List<SyncSessionNote>, incoming: List<SyncSessionNote>, now: Instant): List<SyncSessionNote> =
        SessionNoteRules.merge(held, SessionNoteRules.clean(incoming), now)

    /** The window and the tombstones' 90 days applied to what is kept. */
    fun prune(notes: List<SyncSessionNote>, now: Instant): List<SyncSessionNote> = SessionNoteRules.forFile(notes, now)
}

/**
 * Session notes in a phone backup (AR1c, owner 2026-11-14 as on the PC): only inside a password-protected backup, never a plain one. A backup
 * without a password (the auto-backup, a plain export) carries no note text at all.
 */
object SessionNotesBackup {
    /** The `sessionNotes` part of a Tasks backup: null unless [includesNotes] (a password was set). */
    fun part(notes: List<SyncSessionNote>, includesNotes: Boolean, now: Instant): JsonArray? =
        if (!includesNotes) null else SyncFileJson.writeSessionNotes(SessionNoteRules.forFile(notes, now))

    /** The notes a backup's part holds, cleaned like a file's (a broken entry is left out); empty for no part. */
    fun read(part: JsonElement?): List<SyncSessionNote> = SessionNoteRules.clean(SyncFileJson.readSessionNotes(part).orEmpty())
}
