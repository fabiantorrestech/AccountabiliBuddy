package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SessionTaskItem

/**
 * What the task detail sheet edits in one task: the same fields on the next session's list and on the Later list. A field
 * the sheet did not change is not written, so an edit never overwrites what another device changed meanwhile.
 *
 * @property estimateMinutes The user's estimate; null for none.
 * @property note The private note; empty for none.
 * @property dueOn The local due date ("yyyy-MM-dd") of a Later item; null for none.
 */
data class TaskFields(
    val title: String,
    val steps: List<SessionTaskItem>,
    val estimateMinutes: Int?,
    val note: String,
    val category: String?,
    val categoryColor: String?,
    val dueOn: String?,
) {
    companion object {
        /** The longest estimate, in minutes (a day), and note, as on the PC (SessionChecklist). */
        const val MAX_ESTIMATE_MINUTES = 24 * 60
        const val MAX_NOTE_LENGTH = 4000

        /** An estimate as it is kept: none for 0 or less, held to a day. */
        fun cleanEstimate(minutes: Int?): Int? = minutes?.takeIf { it > 0 }?.coerceAtMost(MAX_ESTIMATE_MINUTES)

        /** A note as it is kept: trimmed, null when blank, cut to [MAX_NOTE_LENGTH]. */
        fun cleanNote(note: String?): String? = note?.trim()?.takeIf { it.isNotEmpty() }?.take(MAX_NOTE_LENGTH)
    }
}
