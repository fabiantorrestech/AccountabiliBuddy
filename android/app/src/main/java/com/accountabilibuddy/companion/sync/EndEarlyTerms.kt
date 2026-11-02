package com.accountabilibuddy.companion.sync

/**
 * What ending the PC's session early takes, by the PC's own rules: wait [waitSeconds] (counted from when the PC
 * answered, [receivedAtElapsed] on this phone's monotonic clock), then a reason of at least [minReasonLength] characters.
 * Through the sync files (ADR 42) the wait ends at [allowedFrom] on the PC's clock instead.
 */
data class EndEarlyTerms(
    val waitSeconds: Int,
    val minReasonLength: Int,
    val note: String?,
    val receivedAtElapsed: Long,
    val allowedFrom: java.time.Instant? = null,
    /** The PC the offer is from (it may not be the one picked in Devices); null when not known. */
    val pcName: String? = null,
)
