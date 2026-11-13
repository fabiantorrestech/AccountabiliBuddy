package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/** A year rolled up by a PC from every device's month files: [months] says which versions it holds. */
data class GoalHistoryYear(
    val formatVersion: Int,
    val year: Int,
    val rolledBy: UUID,
    val rolledAt: OffsetDateTime,
    val months: List<GoalHistoryPiece>,
    val entries: GoalLists,
) {
    val key: GoalHistoryFileKey get() = GoalHistoryFileKey.forYear(year, rolledBy)

    companion object {
        const val CURRENT_FORMAT_VERSION = 1
    }
}
