package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * What the late joiner's rule needs ([GoalHistoryRules.latePrunable], the last `merge` overload; the PC's `GoalHistoryLatePrune`).
 * [firstDropAt]: when the first merge that dropped a row of an unknown goal ran (null for none yet); [active]: the active devices;
 * [self]: this device's id; [lastRegularMerge]: when each device's regular sync file (with goal parts) last merged here.
 */
data class GoalHistoryLatePrune(
    val firstDropAt: OffsetDateTime?,
    val active: List<GoalHistoryDevice>,
    val self: UUID,
    val lastRegularMerge: Map<UUID, OffsetDateTime>,
)
