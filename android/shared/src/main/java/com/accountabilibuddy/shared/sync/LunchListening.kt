package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime

/** A planned lunch window on this device's wall clock (the plan's flexible break or fixed lunch). */
data class LunchWindow(val start: LocalDateTime, val end: LocalDateTime)

/**
 * Whether a device listens for sync files fast because a lunch is running or about to start (LU1-fix; the PC's LunchListening is the same rule).
 * A lunch is started on any device, so every device reads often while one runs or is due, and only then (never all night, to spare the relay's
 * request limit and the battery). Listening when a lunch runs here or in any received file; or, when no lunch was taken today, from [LEAD]
 * before a planned lunch window until its end.
 */
object LunchListening {
    /** How long before a planned lunch window the listening starts. */
    val LEAD: Duration = Duration.ofHours(1)

    /** A lunch whose start is further ahead than this is a clock that runs ahead, not a lunch that runs. */
    val FUTURE_TOLERANCE: Duration = Duration.ofMinutes(2)

    /**
     * [own]: today's lunch on this device, once started (it may be over). [received]: the lunch in each other device's latest received file.
     * [planned]: today's planned lunch windows; [localNow] is [now] on this device's wall clock.
     */
    fun isListening(now: Instant, localNow: LocalDateTime, own: SyncLunch?, received: List<SyncLunch?>, planned: List<LunchWindow>): Boolean {
        if (running(own, now) || received.any { running(it, now) }) {
            return true
        }

        // A lunch taken today (and over) is not expected again.
        if (own != null) {
            return false
        }

        return planned.any { !localNow.isBefore(it.start.minus(LEAD)) && localNow.isBefore(it.end) }
    }

    /** True while [lunch] runs: started (a clock a little ahead counts) and not over. */
    fun running(lunch: SyncLunch?, now: Instant): Boolean =
        lunch != null && !lunch.start.minus(FUTURE_TOLERANCE).isAfter(now) && now.isBefore(lunch.end)

    /**
     * A lunch from another device with its edit stamp held to [now]: a device whose clock runs ahead stamps its edits in the future, and its
     * lunch would then beat every real edit made later on the other devices. A stamp (or, with none, a start) more than [FUTURE_TOLERANCE]
     * ahead becomes [now].
     */
    fun clampFuture(lunch: SyncLunch, now: Instant): SyncLunch =
        if (lunch.lastEdit.isAfter(now.plus(FUTURE_TOLERANCE))) lunch.copy(editedAt = now) else lunch
}
