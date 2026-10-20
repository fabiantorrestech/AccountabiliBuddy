package com.accountabilibuddy.companion.link

/**
 * What ending the PC's session early takes, by the PC's own rules: wait [waitSeconds] (counted from when the PC
 * answered, [receivedAtElapsed] on this phone's monotonic clock), then a reason of at least [minReasonLength] characters.
 */
data class EndEarlyTerms(val waitSeconds: Int, val minReasonLength: Int, val note: String?, val receivedAtElapsed: Long)
