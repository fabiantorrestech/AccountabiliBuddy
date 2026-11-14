package com.accountabilibuddy.shared.sync

import java.time.Instant

/** What a lunch answer does to the session's lock (LunchLockLift): lift it for the new piece, ask for it again, or leave it. */
enum class LunchLockChange {
    NONE,

    /** A piece starts: the lock is lifted for it (when lifting is set up). */
    LIFT,

    /** The running piece ended (counted or discarded): the lock is asked for again, exactly as when lunch ends by itself. */
    RELOCK,
}

/** The lunch after an answer, and what the lock does about it. */
data class LunchOutcome(val lunch: SyncLunch, val lockChange: LunchLockChange)

/** The "End lunch?" question as the phone and the watch show it (ADR 88): wording, whether Discard it is offered. */
data class LunchQuestion(
    val title: String,
    val usedText: String,
    val countText: String,
    val discardText: String?,
    val canDiscard: Boolean,
)

/**
 * What the phone's lunch ✕ does (LU1c, ADR 88), as pure rules over [LunchDay] so the phone, the watch's messages and the checks use the
 * same ones. Count it and Discard it end the running piece, so the lock comes back as when lunch ends; "Start the rest" runs only for what
 * is left of the day's allowance, so the lock is lifted for that piece and never for longer than the allowance. Times come from the caller
 * (trusted time); [fallbackMinutes] is the allowance of a record that carries none (an older app's).
 */
object LunchOps {
    /** "Count it" and "I'm back": the minutes so far count, the rest stays for later. Null when nothing runs. */
    fun count(lunch: SyncLunch, now: Instant): LunchOutcome? =
        LunchDay.count(lunch, now)?.let { LunchOutcome(it, LunchLockChange.RELOCK) }

    /** "Discard it": the running piece never happened. Null when nothing runs or [allowedByMode] is off. */
    fun discard(lunch: SyncLunch, now: Instant, allowedByMode: Boolean): LunchOutcome? =
        LunchDay.discard(lunch, now, allowedByMode)?.let { LunchOutcome(it, LunchLockChange.RELOCK) }

    /** "Start the rest": a new piece for what is left of the day's allowance. Null when a piece runs or too little is left. */
    fun startRest(lunch: SyncLunch, now: Instant, fallbackMinutes: Int): LunchOutcome? =
        LunchDay.startAgain(lunch, now, fallbackMinutes)?.let { LunchOutcome(it, LunchLockChange.LIFT) }

    /** "Start the rest" shows: a lunch was started today, nothing runs and at least a minute is left. */
    fun canStartRest(lunch: SyncLunch?, now: Instant, fallbackMinutes: Int): Boolean =
        lunch != null && LunchDay.canStart(lunch, now, fallbackMinutes)

    /** "Discard it" shows. */
    fun canDiscard(lunch: SyncLunch?, now: Instant, allowedByMode: Boolean): Boolean = LunchDay.canDiscard(lunch, now, allowedByMode)

    /** Whole minutes left of the day's allowance, rounded up as the PC's "40 min left". */
    fun leftMinutes(lunch: SyncLunch, now: Instant, fallbackMinutes: Int): Int =
        LunchDay.ceilMinutes(LunchDay.remaining(lunch, now, fallbackMinutes))

    /**
     * The ✕'s question ("Started 12:05 PM · 20 of 60 min used"), read fresh; null when no piece runs. [time] formats an instant as 12-hour
     * with AM/PM (the caller's clock format).
     */
    fun question(lunch: SyncLunch?, now: Instant, fallbackMinutes: Int, allowedByMode: Boolean, time: (Instant) -> String): LunchQuestion? {
        if (lunch == null || !LunchDay.isRunning(lunch, now)) return null
        val used = Math.floorDiv(LunchDay.used(lunch, now).seconds, 60L).toInt()
        val allowance = LunchDay.allowanceMinutes(lunch, fallbackMinutes)
        val later = Math.max(0, allowance - used)
        val canDiscard = LunchDay.canDiscard(lunch, now, allowedByMode)
        return LunchQuestion(
            title = "End lunch?",
            usedText = "Started ${time(lunch.start)} · $used of $allowance min used",
            countText = if (later > 0) "Count it: $used min count, $later min stay for later today." else "Count it: $used min count.",
            discardText = if (canDiscard) "Discard it: as if lunch never started." else null,
            canDiscard = canDiscard,
        )
    }
}
