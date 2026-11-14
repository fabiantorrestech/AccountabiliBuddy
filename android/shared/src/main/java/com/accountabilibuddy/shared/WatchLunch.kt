package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant

/**
 * Today's flexible lunch as the phone has it, for the watch's Lunch page: whether today is a workday, the lunch
 * length setting, and today's lunch once started ([start] and [end]; null before). Only times, never anything else.
 */
data class WatchLunch(
    val workday: Boolean,
    val lengthMinutes: Int,
    val start: Instant? = null,
    val end: Instant? = null,
    /** "Allow discarding a lunch" (ADR 88) in the phone's current mode: the Lunch page offers Discard it only when true (an older phone sends none: false). */
    val discardAllowed: Boolean = false,
    /** The day's lunch in minutes (ADR 88); null from an older phone, which cannot split lunch: no "Start the rest". */
    val allowanceMinutes: Int? = null,
    /** Minutes of the earlier counted pieces; [start] and [end] are the current or last piece. */
    val countedMinutes: Int = 0,
) {
    /** True while lunch runs at [now]. */
    fun isRunning(now: Instant): Boolean = start != null && end != null && now.isBefore(end)

    /** True once today's lunch was taken and is over. */
    fun isDone(now: Instant): Boolean = start != null && end != null && !now.isBefore(end)

    /** The Lunch page shows on a workday, or while lunch runs. */
    fun isShown(now: Instant): Boolean = workday || isRunning(now)

    /** −5 min can still take something off: the running lunch ends more than a minute from now. */
    fun canShorten(now: Instant): Boolean = isRunning(now) && end!!.isAfter(now.plus(SHORTEST_LEFT))

    /** "Discard it" is offered: lunch runs and the phone's mode allows it. */
    fun canDiscard(now: Instant): Boolean = isRunning(now) && discardAllowed

    /** Whole minutes of the day's lunch used at [now]: the counted pieces plus the current piece so far (a discarded piece is zero length). */
    fun usedMinutes(now: Instant): Int {
        val piece = if (start != null && end != null && now.isAfter(start)) Duration.between(start, minOf(now, end)).seconds / 60 else 0L
        return countedMinutes + piece.toInt()
    }

    /** Whole minutes of the day's lunch still left at [now], never negative; 0 from an older phone. */
    fun leftMinutes(now: Instant): Int = allowanceMinutes?.let { (it - usedMinutes(now)).coerceAtLeast(0) } ?: 0

    /** "Start the rest (N min left)" shows: a piece was counted or discarded and at least a minute is left. */
    fun canStartRest(now: Instant): Boolean = isDone(now) && allowanceMinutes != null && leftMinutes(now) >= 1

    /**
     * What the phone will make of [action] as the Lunch page shows it at once (the phone's next snapshot confirms it); the watch's
     * Discard it does nothing unless [discardAllowed], as on the phone.
     */
    fun expected(action: String, now: Instant): WatchLunch {
        val end = end
        val start = start
        return when {
            action == LunchRemote.START && start == null -> copy(start = now, end = now.plus(Duration.ofMinutes(lengthMinutes.toLong())), allowanceMinutes = lengthMinutes, countedMinutes = 0)
            action == LunchRemote.REST && canStartRest(now) ->
                copy(start = now, end = now.plus(Duration.ofMinutes(leftMinutes(now).toLong())), countedMinutes = usedMinutes(now))
            !isRunning(now) || end == null || start == null -> this
            action == LunchRemote.BACK || action == LunchRemote.COUNT -> copy(end = now)
            action == LunchRemote.DISCARD -> if (discardAllowed) copy(end = start) else this
            action == LunchRemote.PLUS -> copy(end = end.plus(ADJUST_STEP))
            action == LunchRemote.MINUS -> {
                val soonest = now.plus(SHORTEST_LEFT)
                val shorter = end.minus(ADJUST_STEP)
                copy(end = if (shorter < soonest) minOf(soonest, end) else shorter)
            }
            else -> this
        }
    }

    /** How much of the running lunch is left (0 to 1), for the countdown ring. */
    fun fractionLeft(now: Instant): Float {
        if (start == null || end == null || !now.isBefore(end)) return 0f
        val total = Duration.between(start, end).seconds.coerceAtLeast(1)
        return (Duration.between(now, end).seconds.toFloat() / total).coerceIn(0f, 1f)
    }

    companion object {
        /** A shortened lunch ends no sooner than this after now (as on the PC). */
        val SHORTEST_LEFT: Duration = Duration.ofMinutes(1)

        private val ADJUST_STEP: Duration = Duration.ofMinutes(5)
    }
}
