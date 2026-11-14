package com.accountabilibuddy.shared.tasks

/**
 * The small decisions of the phone's Archive page, pure so the checks can run them (AR1c review fix): what a tick box does for each kind of line,
 * which call a delete goes through, which deletes ask first, the one key each list row gets (two id-less steps must not share one, a LazyColumn
 * throws on a repeated key), and when the Tasks page below may start a drag (never while the Archive is open over it).
 */
object ArchiveUiRules {
    /** The tick box of a line: a come-back ticks or unticks, an unfinished past task is ticked, a done one unticked; anything else does nothing. */
    fun tick(
        kind: ArchiveKind,
        isTicked: Boolean,
        id: String,
        tickParked: (String) -> Unit,
        untickParked: (String) -> Unit,
        tickPast: (String) -> Unit,
        untickPast: (String) -> Unit,
    ) {
        when (kind) {
            ArchiveKind.COMEBACK -> if (isTicked) untickParked(id) else tickParked(id)
            ArchiveKind.UNFINISHED -> tickPast(id)
            ArchiveKind.DONE -> untickPast(id)
            else -> Unit
        }
    }

    /** The delete of a line: a note (every device), a come-back, or a past task (a settle record, or the keeper's removal); steps have no delete. */
    fun delete(item: ArchiveItem, deleteNote: (String) -> Unit, deleteComeback: (String) -> Unit, deletePast: (String) -> Unit) {
        when (item.kind) {
            ArchiveKind.NOTE -> deleteNote(item.id)
            ArchiveKind.COMEBACK -> deleteComeback(item.id)
            ArchiveKind.UNFINISHED, ArchiveKind.DONE -> deletePast(item.id)
            else -> Unit
        }
    }

    /** A delete asks first for a note (it reaches every device), a come-back, and a past task only this phone holds (no Undo, no Recently deleted line). */
    fun deleteNeedsConfirm(item: ArchiveItem): Boolean = item.kind == ArchiveKind.NOTE || item.kind == ArchiveKind.COMEBACK || item.keptHere

    /** The question a confirmed delete asks. */
    fun deleteTitle(item: ArchiveItem): String = when (item.kind) {
        ArchiveKind.NOTE -> "Delete this note?"
        ArchiveKind.COMEBACK -> "Delete this come-back?"
        else -> "Delete this task?"
    }

    /** One key per row: the line's kind, parent and id, and a counter after a repeat of the same, so a repeated id never repeats a key. */
    fun rowKeys(items: List<ArchiveItem>): List<String> = uniqueKeys(items.map { "${it.kind}:${it.parentId.orEmpty()}:${it.id}" })

    /** [bases] made unique, in order: the second "x" becomes "x#1", the third "x#2" (and never a name already taken). */
    fun uniqueKeys(bases: List<String>): List<String> {
        val taken = HashSet<String>()
        val repeats = HashMap<String, Int>()
        return bases.map { base ->
            var key = base
            while (!taken.add(key)) {
                val next = (repeats[base] ?: 0) + 1
                repeats[base] = next
                key = "$base#$next"
            }
            key
        }
    }

    /** The Tasks page's long-press lift: only while the task list is open, no sheet, the Shelf not hidden, no drag running, and the Archive closed. */
    fun canLift(modelActive: Boolean, sheetOpen: Boolean, hidden: Boolean, dragActive: Boolean, archiveOpen: Boolean): Boolean =
        modelActive && !sheetOpen && !hidden && !dragActive && !archiveOpen
}
