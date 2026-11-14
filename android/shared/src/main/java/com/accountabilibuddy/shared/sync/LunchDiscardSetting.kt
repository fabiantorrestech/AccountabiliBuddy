package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/** What [LunchDiscardSetting.change] did with a wish. */
enum class LunchDiscardOutcome {
    /** The wish is in effect now (it was the same, or stricter). */
    APPLIED,

    /** A looser wish is waiting for the looser-change delay. */
    WAITING,

    /** A looser wish was refused: a focus session runs. */
    REFUSED,
}

/**
 * "Allow discarding a lunch" for one mode (ADR 88; the PC's Core `LunchDiscardSetting` is the same rule). On by default. Turning it off is
 * stricter: it applies at once and drops a waiting "on". Turning it on is looser: it waits the looser-change delay and is refused while a
 * focus session runs; a waiting change takes effect only when no session runs. Each mode keeps its own copy and each device its own (ADR 69).
 */
data class LunchDiscardSetting(val allowed: Boolean = true, val pending: Boolean? = null, val pendingFrom: Instant? = null) {
    /** Asks for [wanted]. Stricter or the same: applied now. Looser: refused while [sessionRunning], else waiting [wait] (a wait already running for it keeps its time). */
    fun change(wanted: Boolean, now: Instant, wait: Duration, sessionRunning: Boolean): Pair<LunchDiscardSetting, LunchDiscardOutcome> = when {
        !isLooser(allowed, wanted) -> LunchDiscardSetting(wanted) to LunchDiscardOutcome.APPLIED
        sessionRunning -> this to LunchDiscardOutcome.REFUSED
        pending == wanted && pendingFrom != null -> this to LunchDiscardOutcome.WAITING
        else -> copy(pending = wanted, pendingFrom = now.plus(wait)) to LunchDiscardOutcome.WAITING
    }

    /** A looser change that waited long enough takes effect, only while no session runs. */
    fun applyIfReady(now: Instant, sessionRunning: Boolean): LunchDiscardSetting {
        val due = pendingFrom
        val wanted = pending
        return if (!sessionRunning && wanted != null && due != null && !now.isBefore(due)) LunchDiscardSetting(wanted) else this
    }

    companion object {
        /** On is looser than off. */
        fun isLooser(from: Boolean, to: Boolean): Boolean = to && !from
    }
}
