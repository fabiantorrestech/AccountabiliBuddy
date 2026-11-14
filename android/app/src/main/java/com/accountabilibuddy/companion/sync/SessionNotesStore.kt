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
    private var unreadableChars = 0
    private val _notes = MutableStateFlow(read())

    /**
     * How many characters of stored notes this run could not open (the Keystore key was lost or the value is damaged), 0 when all is well. While it is above 0 the
     * store is read-only for the run: nothing is written, so the sealed value stays as it is (a tombstone not yet sent is not lost to an overwrite). A count only, never text.
     */
    val unreadableCount: Int get() = unreadableChars

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
        return save(merged)
    }

    /** Changes a note's text on every device; a blank text, an unknown note and an unchanged text change nothing. */
    fun edit(id: String, text: String) {
        val changed = synchronized(this) {
            val changedNotes = SessionNoteEdits.edit(_notes.value, id, text, now())
            if (changedNotes == _notes.value) false else save(changedNotes)
        }
        if (changed) onChanged()
    }

    /** Deletes a note on every device: its text is gone at once and a tombstone tells the others. */
    fun delete(id: String) {
        val changed = synchronized(this) {
            val changedNotes = SessionNoteEdits.delete(_notes.value, id, now())
            if (changedNotes == _notes.value) false else save(changedNotes)
        }
        if (changed) onChanged()
    }

    // The window and the tombstones' 90 days are applied on every save, as on the PC.
    // Sealed first, held after: a Keystore failure leaves both the flow and the stored value as they were, and says so (false). Nothing is written while the stored
    // notes could not be opened (they would be overwritten by a list that lacks them).
    private fun save(notes: List<SyncSessionNote>): Boolean {
        if (unreadableChars > 0) return false
        val kept = SessionNoteRules.forFile(notes, now())
        val sealed = runCatching { TokenVault.seal(SyncFileJson.writeSessionNotes(kept).toString(), ALIAS) }.getOrNull() ?: return false
        _notes.value = kept
        prefs.edit().putString(KEY, sealed).apply()
        return true
    }

    // Empty when nothing is stored. When something is stored but can't be opened (the key is gone, the value is damaged), the list is empty for this run, the
    // sealed value is kept untouched and the store is read-only (see [unreadableCount]).
    private fun read(): List<SyncSessionNote> {
        val stored = prefs.getString(KEY, null) ?: return emptyList()
        val json = TokenVault.open(stored, ALIAS)?.let { runCatching { Json.parseToJsonElement(it) as JsonArray }.getOrNull() }
        if (json == null) {
            unreadableChars = stored.length.coerceAtLeast(1)
            return emptyList()
        }

        return SessionNoteRules.clean(SyncFileJson.readSessionNotes(json).orEmpty())
    }

    private companion object {
        const val KEY = "notes"
        const val ALIAS = "accountabilibuddy-session-notes"
    }
}
