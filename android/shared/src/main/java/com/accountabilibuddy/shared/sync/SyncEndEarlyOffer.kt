package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * A PC's answer to the phone's [SyncAction.END_EARLY_BEGIN] (ADR 42), in its file ("endEarly"), as on the PC (Core's
 * SyncEndEarlyOffer): the phone counts down to [allowedFrom] (the PC's clock), asks for a reason of at least
 * [minReasonLength] characters and sends [SyncAction.END_EARLY] with [requestId] and the reason's length only. Taken
 * until [allowedFrom] plus [VALID_FOR], and only in the phase it was made in.
 *
 * @property phaseStartedAt The start of the focus phase it belongs to, as it was when offered.
 * @property note The PC's text for the phone to show; null for none.
 */
data class SyncEndEarlyOffer(
    val requestId: String,
    val phaseStartedAt: Instant,
    val allowedFrom: Instant,
    val minReasonLength: Int,
    val note: String? = null,
) {
    /** True once the PC no longer takes the phone's end-early action for it (an old offer can linger in its file). */
    fun expired(now: Instant): Boolean = now.isAfter(allowedFrom.plus(VALID_FOR))

    companion object {
        /** How long after [allowedFrom] the phone's end-early action is still taken. */
        val VALID_FOR: Duration = Duration.ofMinutes(10)
    }
}
