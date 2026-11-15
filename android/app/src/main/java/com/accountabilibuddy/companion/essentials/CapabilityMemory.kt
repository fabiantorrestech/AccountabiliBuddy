package com.accountabilibuddy.companion.essentials

import java.time.Duration
import java.time.Instant

/**
 * The last good answer to LockMyPhone-Extended's capabilities query, kept for [grace] when a later query fails once, so
 * one missed query never drops the strict hold path (S3). Bounded on trusted time and, so a moved clock can't stretch it,
 * on the monotonic clock too: the answer is used only while both say less than [grace] has passed. Pure.
 */
class CapabilityMemory<T : Any>(private val grace: Duration) {
    private var value: T? = null
    private var at: Instant? = null
    private var elapsedAtMs = 0L

    fun record(answer: T, now: Instant, elapsedMs: Long) {
        value = answer
        at = now
        elapsedAtMs = elapsedMs
    }

    /** The remembered answer, or null when there is none or it is older than the grace. */
    fun recall(now: Instant, elapsedMs: Long): T? {
        val answer = value ?: return null
        val recorded = at ?: return null
        val trustedAge = Duration.between(recorded, now)
        val monotonicAge = elapsedMs - elapsedAtMs
        // A trusted time before the answer (the clock moved back) leaves the monotonic bound to hold the line.
        val fresh = trustedAge < grace && monotonicAge in 0 until grace.toMillis()
        return answer.takeIf { fresh }
    }

    fun forget() {
        value = null
        at = null
    }
}
