package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/** One device's entries of one UTC month, as its history file holds them. [entries] holds only entries (and newer entry items). */
data class GoalHistoryMonth(
    val formatVersion: Int,
    val deviceId: UUID,
    val year: Int,
    val month: Int,
    val writtenAt: OffsetDateTime,
    val entries: GoalLists,
) {
    val key: GoalHistoryFileKey get() = GoalHistoryFileKey.forMonth(deviceId, year, month)

    companion object {
        const val CURRENT_FORMAT_VERSION = 1
    }
}
