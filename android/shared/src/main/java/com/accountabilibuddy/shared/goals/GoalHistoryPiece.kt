package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/** One device's month file at one version, rolled into a year file. */
data class GoalHistoryPiece(val deviceId: UUID, val month: Int, val writtenAt: OffsetDateTime)
