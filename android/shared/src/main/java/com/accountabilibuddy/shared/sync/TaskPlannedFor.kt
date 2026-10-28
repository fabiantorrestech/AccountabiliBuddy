package com.accountabilibuddy.shared.sync

/**
 * The day part a task was put in the plan for (ADR 50, "Put in plan"), as the PC writes it: the part's local [date]
 * ("yyyy-MM-dd"), the plan part's id ([partId], 36 characters) and the part's [start] ("HH:mm:ss") as it was chosen. The
 * phone has no screen for it yet; it keeps what it read, as written, so a merge or an edit here never loses it.
 */
data class TaskPlannedFor(val date: String, val partId: String, val start: String) {
    /** The slot as the PC builds it for a merge tie-break (never shown or logged). */
    val tieText: String get() = listOf(date, partId.lowercase(), start).joinToString("\u0003")
}
