package com.accountabilibuddy.shared.sync

import java.time.OffsetDateTime
import java.util.UUID

/**
 * Which year file (year, rolling PC) a device has merged, and its version (ADR 79; the PC's `SyncGoalYearMerged`): ids, numbers and times
 * only. A month file is removed only once every active device's marker lists its year file at that version or later.
 */
data class SyncGoalYearMerged(val year: Int, val rolledBy: UUID, val rolledAt: OffsetDateTime)
