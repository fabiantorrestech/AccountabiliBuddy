package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * The day's lunch as one pure rule (ADR 88; the PC's Core `LunchDay` is the same rule). A day has an allowance (the lunch length when the
 * first piece started, +5 and -5 moving it) that can be taken in pieces: "Count it" ends the running piece and counts it, the rest stays
 * available for a later piece; "Discard it" takes the running piece back as if it never started, when the mode's setting allows it. Every
 * device reads the same record, so "40 left" is the same on all of them whatever each one's own length setting says. No piece ever takes the
 * day's total past [MAX_LENGTH]. Times come from the caller (the phone's trusted time).
 */
object LunchDay {
    /** The day's lunch is never longer than this in total, whatever +5, another device or a stale file says. */
    const val MAX_MINUTES = 120
    val MAX_LENGTH: Duration = Duration.ofMinutes(MAX_MINUTES.toLong())

    /** How much +5 / -5 min changes a running lunch by. */
    val ADJUST_STEP: Duration = Duration.ofMinutes(5)

    /** A shortened piece ends no sooner than this after now, and a new piece needs at least this much left. */
    val SHORTEST_LEFT: Duration = Duration.ofMinutes(1)

    /** The counted pieces added up (the current piece is not in them). */
    fun countedTotal(lunch: SyncLunch): Duration = (lunch.counted ?: emptyList()).fold(Duration.ZERO) { sum, piece -> sum.plus(piece.length) }

    /** The day's lunch in minutes: the record's own, else [fallbackMinutes] (an older app's record carries none); 0 to 120. */
    fun allowanceMinutes(lunch: SyncLunch, fallbackMinutes: Int): Int = (lunch.allowance ?: fallbackMinutes).coerceIn(0, MAX_MINUTES)

    /** The lunch used so far: the counted pieces plus the current piece up to [now] (or its end). A discarded piece used nothing. */
    fun used(lunch: SyncLunch, now: Instant): Duration {
        val current = if (lunch.discarded) Duration.ZERO else maxOf(Duration.ZERO, Duration.between(lunch.start, minOf(now, lunch.end)))
        return countedTotal(lunch).plus(current)
    }

    /** What is left of the day's allowance, never negative. */
    fun remaining(lunch: SyncLunch, now: Instant, fallbackMinutes: Int): Duration =
        maxOf(Duration.ZERO, Duration.ofMinutes(allowanceMinutes(lunch, fallbackMinutes).toLong()).minus(used(lunch, now)))

    /** True while a piece runs: not discarded, and not over. */
    fun isRunning(lunch: SyncLunch?, now: Instant): Boolean = lunch != null && !lunch.discarded && now.isBefore(lunch.end)

    /** Another piece can start: nothing runs and at least [SHORTEST_LEFT] of the allowance is left (a day with no lunch yet can always start). */
    fun canStart(lunch: SyncLunch?, now: Instant, fallbackMinutes: Int): Boolean =
        lunch == null || (!isRunning(lunch, now) && remaining(lunch, now, fallbackMinutes) >= SHORTEST_LEFT)

    /** "Discard it" is offered: a piece runs and the mode's setting allows it ([allowedByMode]); any time in the piece. */
    fun canDiscard(lunch: SyncLunch?, now: Instant, allowedByMode: Boolean): Boolean = allowedByMode && isRunning(lunch, now)

    /** A fixed lunch starts by itself only on a day with no lunch record at all: a discarded lunch is still a record, so it never starts again by itself. */
    fun autoStartPending(lunch: SyncLunch?): Boolean = lunch == null

    /** The first piece of the day: runs [length] from [now] (held to 1 minute to 120), and that length (rounded up to a minute) is the day's allowance. */
    fun begin(now: Instant, length: Duration): SyncLunch {
        val held = maxOf(SHORTEST_LEFT, minOf(length, MAX_LENGTH))
        val minutes = Math.ceil(held.toMillis() / 60000.0).toInt()
        return SyncLunch(now, now.plus(held), now, minutes, emptyList(), false)
    }

    /** A new piece after the last one: it runs for what is left. Null when a piece runs or too little is left. */
    fun startAgain(lunch: SyncLunch, now: Instant, fallbackMinutes: Int): SyncLunch? {
        if (isRunning(lunch, now)) return null
        val remaining = remaining(lunch, now, fallbackMinutes)
        if (remaining < SHORTEST_LEFT) return null
        val counted = (lunch.counted ?: emptyList()).toMutableList()
        if (!lunch.discarded && lunch.end.isAfter(lunch.start)) counted.add(LunchPiece(lunch.start, lunch.end))
        return held(SyncLunch(now, now.plus(remaining), now, allowanceMinutes(lunch, fallbackMinutes), counted, false))
    }

    /** "Count it" (and "I'm back"): the running piece ends now and counts; the rest of the allowance stays for later. Null when nothing runs. */
    fun count(lunch: SyncLunch, now: Instant): SyncLunch? =
        if (isRunning(lunch, now)) lunch.copy(end = maxOf(now, lunch.start), editedAt = now) else null

    /**
     * "Discard it": the running piece never happened. Null when nothing runs or the mode doesn't allow it. The first piece becomes a zero-length
     * record marked discarded (the day stays taken); a later piece goes back to the last counted one, which leaves the list.
     */
    fun discard(lunch: SyncLunch, now: Instant, allowedByMode: Boolean): SyncLunch? {
        if (!canDiscard(lunch, now, allowedByMode)) return null
        val counted = lunch.counted ?: emptyList()
        if (counted.isNotEmpty()) {
            val last = counted.last()
            return lunch.copy(start = last.start, end = last.end, counted = counted.dropLast(1), discarded = false, editedAt = now)
        }

        return lunch.copy(end = lunch.start, discarded = true, editedAt = now)
    }

    /**
     * +5 ([change] positive) or -5 on the running piece, the allowance moving with it: never ends sooner than a minute from now, a -5 never
     * makes it longer, and the day's total never goes past [MAX_LENGTH]. Null when nothing runs or nothing changes.
     */
    fun adjust(lunch: SyncLunch, now: Instant, change: Duration, fallbackMinutes: Int): SyncLunch? {
        if (!isRunning(lunch, now)) return null
        val end = lunch.end
        var newEnd = end.plus(change)
        val latest = lunch.start.plus(maxOf(Duration.ZERO, MAX_LENGTH.minus(countedTotal(lunch))))
        if (newEnd.isAfter(latest)) newEnd = if (latest.isAfter(end)) latest else end
        val soonest = now.plus(SHORTEST_LEFT)
        if (newEnd.isBefore(soonest)) newEnd = if (soonest.isBefore(end)) soonest else end
        if (newEnd == end) return null
        val moved = Math.round(Duration.between(end, newEnd).toMillis() / 60000.0).toInt()
        return lunch.copy(end = newEnd, editedAt = now, allowance = (allowanceMinutes(lunch, fallbackMinutes) + moved).coerceIn(0, MAX_MINUTES))
    }

    /** The record held to its limits: the allowance 0 to 120, and the current piece ends so that the day's total is at most [MAX_LENGTH]. */
    fun held(lunch: SyncLunch): SyncLunch {
        val latestEnd = lunch.start.plus(maxOf(Duration.ZERO, MAX_LENGTH.minus(countedTotal(lunch))))
        val held = if (lunch.end.isAfter(latestEnd)) lunch.copy(end = latestEnd) else lunch
        val allowance = held.allowance
        return if (allowance != null && (allowance < 0 || allowance > MAX_MINUTES)) held.copy(allowance = allowance.coerceIn(0, MAX_MINUTES)) else held
    }

    /**
     * The lunch to take from another device's file, or null when nothing changes here (the caller checks that it is today's, and ignores a
     * record identical to the one last received from the same sender). The stamp is held to [now] ([LunchListening.clampFuture]) and the day's
     * total to [MAX_LENGTH]. A record whose `counted` is missing (not empty) is an older app's edit: when its start is ours it keeps our pieces
     * and allowance (the +5 or End on piece 2 must not wipe the counted minutes), and when its start is one of our counted pieces it is a stale
     * view of the day and changes nothing. Otherwise the newer edit wins, and the same pieces change nothing.
     */
    fun receive(ours: SyncLunch?, received: SyncLunch, now: Instant): SyncLunch? {
        var lunch = LunchListening.clampFuture(received, now)
        if (lunch.counted == null && ours != null) {
            if ((ours.counted ?: emptyList()).any { it.start == lunch.start }) return null
            if (lunch.start == ours.start) {
                lunch = lunch.copy(counted = ours.counted, allowance = ours.allowance ?: lunch.allowance, discarded = ours.discarded)
                val allowance = lunch.allowance
                if (allowance != null) {
                    val piece = if (lunch.end.isAfter(lunch.start)) Duration.between(lunch.start, lunch.end) else Duration.ZERO
                    val needed = Math.ceil(countedTotal(lunch).plus(piece).toMillis() / 60000.0).toInt()
                    lunch = lunch.copy(allowance = minOf(MAX_MINUTES, maxOf(allowance, needed)))
                }
            }
        }

        lunch = held(lunch)
        return if (ours != null && (ours.lastEdit >= lunch.lastEdit || samePieces(ours, lunch))) null else lunch
    }

    /** The same lunch apart from the edit stamp: current piece, allowance, discarded mark and counted pieces (missing and empty alike). */
    fun samePieces(a: SyncLunch, b: SyncLunch): Boolean =
        a.start == b.start && a.end == b.end && a.allowance == b.allowance && a.discarded == b.discarded &&
            (a.counted ?: emptyList<LunchPiece>()) == (b.counted ?: emptyList<LunchPiece>())

    /**
     * Whether the device whose lunch is [theirs] has [ours] (LunchDelivery): the same current piece, and the same counted pieces and allowance
     * when theirs sends them (an older app sends neither, so it is judged on the current piece alone).
     */
    fun hasPieces(theirs: SyncLunch, ours: SyncLunch): Boolean =
        theirs.start == ours.start && theirs.end == ours.end && theirs.discarded == ours.discarded &&
            (theirs.counted == null || theirs.counted == (ours.counted ?: emptyList<LunchPiece>())) &&
            (theirs.allowance == null || theirs.allowance == ours.allowance)
}
