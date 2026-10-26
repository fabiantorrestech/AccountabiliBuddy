package com.accountabilibuddy.watch.timers

import java.time.Duration
import java.time.Instant

/**
 * One of the watch's own timers (V1: kept on the watch, never sent anywhere). It keeps its end time while running and
 * its time left while paused, so it survives a restart. Running past its end, it's done ("Time's up") until dismissed.
 */
data class WatchTimer(
    val id: Int,
    val name: String,
    val length: Duration,
    val endsAt: Instant?,
    val pausedLeft: Duration?,
) {
    val isPaused: Boolean get() = endsAt == null

    fun isDone(now: Instant): Boolean = endsAt != null && !endsAt.isAfter(now)

    fun isCounting(now: Instant): Boolean = endsAt != null && endsAt.isAfter(now)

    /** The time left: to the end while running, as it was while paused, zero once done. */
    fun left(now: Instant): Duration = when {
        endsAt != null -> Duration.between(now, endsAt).coerceAtLeast(Duration.ZERO)
        else -> pausedLeft ?: length
    }

    /** How much is left of the whole length (1 = just started, 0 = done). */
    fun fractionLeft(now: Instant): Float =
        if (length.isZero) 0f else (left(now).toMillis().toFloat() / length.toMillis()).coerceIn(0f, 1f)

    fun paused(now: Instant): WatchTimer = copy(endsAt = null, pausedLeft = left(now))

    fun resumed(now: Instant): WatchTimer = copy(endsAt = now.plus(pausedLeft ?: length), pausedLeft = null)

    /** A minute more: from now when it's done, else onto what's left (the length grows so the ring stays honest). */
    fun plusMinute(now: Instant): WatchTimer {
        val minute = Duration.ofMinutes(1)
        return when {
            isDone(now) -> copy(length = minute, endsAt = now.plus(minute))
            endsAt != null -> copy(length = length.plus(minute), endsAt = endsAt.plus(minute))
            else -> copy(length = length.plus(minute), pausedLeft = (pausedLeft ?: length).plus(minute))
        }
    }

    companion object {
        const val DEFAULT_NAME = "Timer"
    }
}

private fun Duration.coerceAtLeast(minimum: Duration): Duration = if (this < minimum) minimum else this
