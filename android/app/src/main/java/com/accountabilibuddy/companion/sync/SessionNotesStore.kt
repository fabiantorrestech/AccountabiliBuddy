package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SessionNoteEdits
import com.accountabilibuddy.shared.sync.SessionNoteRules
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncSessionNote
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import java.time.Instant
import java.time.temporal.ChronoUnit

/**
 * The session notes of every device (ADR 87, AR1c): the lines typed in Focus mode on the PCs, which reach this phone in the sealed sync files and are
 * shown in the Archive. The phone types no new note; it edits a text and deletes a note, each a stamped change (a tombstone for a delete) that goes
 * out in this phone's file and reaches the PCs ([forSync]). Merged item by item like the PC (newest edit wins; a tombstone is kept for the note's 90-day
 * window). Note text is private: the app's private prefs, sealed with a key in the Android Keystore ([TokenVault]) like the other private phone stores,
 * never logged, never in the watch's snapshot, never in a plain backup (only inside a password-protected one, [SessionNotesBackup]).
 *
 * The rules are the shared pure [SessionNoteEdits] and [SessionNoteRules]; this class only stores their result.
 */
class SessionNotesStore(context: Context, private val now: () -> Instant = { Instant.now().truncatedTo(ChronoUnit.MILLIS) }) {
    private val prefs = context.getSharedPreferences("session_notes", Context.MODE_PRIVATE)
    private val _notes = MutableStateFlow(read())

    /** Every note held, tombstones included (the Archive builder shows the live ones). */
    val notes: StateFlow<List<SyncSessionNote>> = _notes.asStateFlow()

    /** Set by the app: the user changed a note here, so it goes to the other devices soon. */
    var onChanged: () -> Unit = {}

    /** The notes for this phone's sync file (ADR 87): every live note and each tombstone, inside the window. */
    @Synchronized
    fun forSync(): List<SyncSessionNote> = SessionNoteEdits.prune(_notes.value, now())

    /** Takes in the notes of another device's file or a backup. Nothing is written, and the flow doesn't move, when the merge changes nothing. */
    @Synchronized
    fun receive(incoming: List<SyncSessionNote>): Boolean {
        val merged = SessionNoteEdits.receive(_notes.value, incoming, now())
        if (merged == _notes.value) return false
        save(merged)
        return true
    }

    /** Changes a note's text on every device; a blank text, an unknown note and an unchanged text change nothing. */
    fun edit(id: String, text: String) {
        val changed = synchronized(this) {
            val changedNotes = SessionNoteEdits.edit(_notes.value, id, text, now())
            if (changedNotes == _notes.value) false else { save(changedNotes); true }
        }
        if (changed) onChanged()
    }

    /** Deletes a note on every device: its text is gone at once and a tombstone tells the others. */
    fun delete(id: String) {
        val changed = synchronized(this) {
            val changedNotes = SessionNoteEdits.delete(_notes.value, id, now())
            if (changedNotes == _notes.value) false else { save(changedNotes); true }
        }
        if (changed) onChanged()
    }

    // The window and the tombstones' 90 days are applied on every save, as on the PC.
    private fun save(notes: List<SyncSessionNote>) {
        val kept = SessionNoteRules.forFile(notes, now())
        _notes.value = kept
        prefs.edit().putString(KEY, TokenVault.seal(SyncFileJson.writeSessionNotes(kept).toString(), ALIAS)).apply()
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): List<SyncSessionNote> {
        val json = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) }
            ?.let { runCatching { Json.parseToJsonElement(it) as JsonArray }.getOrNull() } ?: return emptyList()
        return SessionNoteRules.clean(SyncFileJson.readSessionNotes(json).orEmpty())
    }

    private companion object {
        const val KEY = "notes"
        const val ALIAS = "accountabilibuddy-session-notes"
    }
}
