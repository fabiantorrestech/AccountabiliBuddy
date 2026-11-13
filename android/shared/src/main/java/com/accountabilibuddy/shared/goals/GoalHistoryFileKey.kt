package com.accountabilibuddy.shared.goals

import java.util.UUID

/** Which history file: a month file of a device, or a year file keyed by the PC that rolled it (the PC's `GoalHistoryFileKey`). */
data class GoalHistoryFileKey(val kind: GoalHistoryFileKind, val deviceId: UUID?, val year: Int, val month: Int?) {
    companion object {
        fun forMonth(deviceId: UUID, year: Int, month: Int) = GoalHistoryFileKey(GoalHistoryFileKind.Month, deviceId, year, month)

        fun forYear(year: Int, rolledBy: UUID) = GoalHistoryFileKey(GoalHistoryFileKind.Year, rolledBy, year, null)
    }
}
