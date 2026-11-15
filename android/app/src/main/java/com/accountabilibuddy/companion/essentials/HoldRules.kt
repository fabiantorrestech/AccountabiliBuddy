package com.accountabilibuddy.companion.essentials

import java.time.Duration
import java.time.Instant

/**
 * When the session's hold is sent to LockMyPhone-Extended (see [SessionLock]). Pure, on trusted time.
 * A hold is sent again after [RESEND_AFTER] even when nothing changed: LockMyPhone-Extended treats a repeat of the same
 * id as "move the end" and restarts a lock that has gone, so a killed or replaced lock comes back at the next
 * reconcile point (refresh or unlock) without any extra loop.
 */
object HoldRules {
    val RENEW_BEFORE: Duration = Duration.ofMinutes(15)
    val RESEND_AFTER: Duration = Duration.ofMinutes(5)

    fun due(
        heldFor: String?,
        holdId: String,
        heldUntilMs: Long,
        untilMs: Long,
        hasSetEnd: Boolean,
        lastSentMs: Long,
        now: Instant,
        force: Boolean = false,
    ): Boolean {
        val nowMs = now.toEpochMilli()
        return force ||
            heldFor != holdId ||
            (hasSetEnd && heldUntilMs != untilMs) ||
            (!hasSetEnd && heldUntilMs - nowMs < RENEW_BEFORE.toMillis()) ||
            // Never sent, or the clock moved back past the last send: send again.
            lastSentMs <= 0L || lastSentMs > nowMs ||
            nowMs - lastSentMs >= RESEND_AFTER.toMillis()
    }
}
