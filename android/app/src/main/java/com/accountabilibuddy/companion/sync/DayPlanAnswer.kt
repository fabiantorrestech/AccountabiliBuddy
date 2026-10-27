package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.model.DayPlan
import java.time.Instant

/**
 * How the PC took the phone's day plan edit (ADR 19; the PC's DayPlanAnswer). [status] is one of [APPLIED],
 * [LOCKED], [CONFLICT], [INVALID], or [UNSUPPORTED] when the PC's app doesn't know the request yet.
 */
data class DayPlanAnswer(
    val status: String,
    val message: String,
    val currentVersion: String? = null,
    val currentPlan: DayPlan? = null,
    val currentEditedAt: Instant? = null,
) {
    companion object {
        const val APPLIED = "applied"
        const val LOCKED = "locked"
        const val CONFLICT = "conflict"
        const val INVALID = "invalid"
        const val UNSUPPORTED = "unsupported"
    }
}
