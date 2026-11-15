package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * Which device of the group has a focus session running, from the sync files received from it (ADR 85, rules R1 to R3, the plan check's B4
 * and the PS1a review's B1 and B2). The Kotlin twin of Core's `GroupSessions`, parity-checked against its `ps1/live` and `ps1/overlap`
 * fixtures. Pure: the caller passes a trusted `now` and, for each file, the trusted time it arrived ([ReceivedSyncFile.receivedAt]). A
 * device's own stamp is never trusted past that arrival: [heardAt].
 */
object GroupSessions {
    /** ADR 60 and ADR 85 R2: a file quiet for longer than this isn't running a session another device should wait for. */
    val QUIET_AFTER: Duration = Duration.ofMinutes(15)

    /** R3: a quiet phone session is reported as "last heard" only for this long; after it the session is taken as over. */
    val STALE_FOR: Duration = Duration.ofHours(4)

    /** A stamp ahead of the receiver's trusted time by no more than this is believed (honest clock skew); a phone file stamped further ahead of now is not live. */
    val AHEAD_TOLERANCE: Duration = Duration.ofMinutes(2)

    // A PC session with no set end counts as running only while its PC file is younger than this (Core's rule).
    private val NO_SET_END_FRESHNESS: Duration = Duration.ofMinutes(30)

    /**
     * When the receiver last heard from a device (any kind): the sender's stamp, but never later than the time its file arrived (a clock
     * running ahead can't make a file look fresh or newer). A stamp up to [AHEAD_TOLERANCE] ahead of the arrival is believed as it is.
     * This is the ONE place a stamp ahead of our clock is read (Q1152 is settled by the PS1a review: held to the arrival).
     */
    fun heardAt(device: SyncDeviceCard, receivedAt: Instant): Instant =
        if (device.writtenAt.isAfter(receivedAt.plus(AHEAD_TOLERANCE))) receivedAt else device.writtenAt

    /**
     * When a device's open session piece ends as a receiver counts it (any device): when it was last heard from ([heardAt]), never after
     * [now], and never after the moment the file says the user went away (`awaySince`: pause, lunch, the device off), so paused time is
     * never focus on the receiver.
     */
    fun openPieceEnd(file: SyncFile, receivedAt: Instant, now: Instant): Instant {
        var end = heardAt(file.device, receivedAt)
        if (now.isBefore(end)) end = now
        val away = file.running?.awaySince
        return if (away != null && away.isBefore(end)) away else end
    }

    /**
     * [file] with each open piece closed where a receiver counts it ([openPieceEnd]), the way the PC's `ReceivedSessions.Closed` does: a piece
     * that starts after that end (a PC's open piece while it plans) is left out, an empty one stays. For readers that take a file's sessions
     * as they are, such as the goals' attribution, which otherwise runs an open piece to `now` (PS1b review S2).
     */
    fun closeOpenPieces(file: SyncFile, receivedAt: Instant, now: Instant): SyncFile {
        val end = openPieceEnd(file, receivedAt, now)
        return file.copy(sessions = file.sessions.mapNotNull { session ->
            when {
                session.end != null -> session
                session.start.isAfter(end) -> null
                else -> session.copy(end = end)
            }
        })
    }

    /**
     * A PC file's running session when it is live at [now] by Core's rule (ADR 60), else null: no `offSince`, heard from within 15 minutes,
     * and: a planning stage within its bound; a set end still ahead; or, with no set end, heard from under 30 minutes ago. A PC stepping away
     * has no exception (its file's end is the end). This is what the `ps1/live` PC cases pin, for COUNTING and parity only: on the phone, R1 and
     * "Running on <PC>" use `PcFileControl.groupLive` (liveRunning + guardsHold), the rule the Today PC card and the lock seam read (Q1158, plan R2).
     */
    fun pcRuleOfCore(received: ReceivedSyncFile, now: Instant): SyncRunning? {
        val running = received.file.running ?: return null
        val heard = heardAt(received.file.device, received.receivedAt)
        if (running.offSince != null || Duration.between(heard, now) > QUIET_AFTER) return null
        val planningEnd = running.planningEndsAt
        val end = running.phaseEndsAt
        val live = when {
            planningEnd != null ->
                now.isBefore(planningEnd.plus(com.accountabilibuddy.shared.PlanningBound.after(running.phaseStartedAt, running.phaseEndsAt)))
            end != null -> now.isBefore(end)
            else -> Duration.between(heard, now) < NO_SET_END_FRESHNESS
        }
        return running.takeIf { live }
    }

    /**
     * [received]'s running session when it is live at [now] (R2), else null. PC files by [pcLive]; phone files: no `offSince`, not stamped
     * more than [AHEAD_TOLERANCE] ahead of [now], heard from within [QUIET_AFTER] ([heardAt]) and not past its set end (a set end that has
     * passed still counts while the user is away, or when there is none). Any other device kind is never live.
     */
    fun liveRunning(received: ReceivedSyncFile, now: Instant, pcLive: (ReceivedSyncFile) -> SyncRunning?): SyncRunning? {
        val running = received.file.running ?: return null
        val device = received.file.device
        return when (device.kind) {
            SyncDeviceCard.KIND_PC -> pcLive(received)
            SyncDeviceCard.KIND_PHONE ->
                running.takeIf {
                    it.offSince == null && !device.writtenAt.isAfter(now.plus(AHEAD_TOLERANCE)) &&
                        Duration.between(heardAt(device, received.receivedAt), now) <= QUIET_AFTER && phaseStillOn(it, now)
                }
            else -> null
        }
    }

    fun isLive(received: ReceivedSyncFile, now: Instant, pcLive: (ReceivedSyncFile) -> SyncRunning?): Boolean = liveRunning(received, now, pcLive) != null

    /**
     * The live session on another device, or null for none (R2). Several live: the one heard from last ([heardAt]; a tie: the lower device
     * id, as the id's text in order).
     *
     * @param others The received files of the other devices (never this device's own).
     */
    fun liveElsewhere(others: List<ReceivedSyncFile>, now: Instant, pcLive: (ReceivedSyncFile) -> SyncRunning?): GroupSessionLive? {
        val newest = others.filter { isLive(it, now, pcLive) }.sortedWith(newestHeardFirst).firstOrNull() ?: return null
        val running = newest.file.running!!
        val endsAt = running.phaseEndsAt?.plus(awayGap(running, now))
        val device = newest.file.device
        return GroupSessionLive(device.deviceId, device.name, device.kind, endsAt, running.awaySince != null, running.phaseEndsAt == null)
    }

    /**
     * A phone session that is quiet (R3): the phone heard from last whose file says a session runs, with no `offSince`, that would be live
     * but for being quiet for longer than [QUIET_AFTER] (and for no longer than [STALE_FOR]); null for none. Start shows its "last heard"
     * line; [GroupSessionStale.lastHeardAt] is [heardAt].
     */
    fun staleElsewhere(others: List<ReceivedSyncFile>, now: Instant): GroupSessionStale? {
        val newest = others.filter { other ->
            val running = other.file.running
            val quiet = Duration.between(heardAt(other.file.device, other.receivedAt), now)
            other.file.device.kind == SyncDeviceCard.KIND_PHONE && running != null && running.offSince == null &&
                quiet > QUIET_AFTER && quiet <= STALE_FOR && phaseStillOn(running, now)
        }.sortedWith(newestHeardFirst).firstOrNull() ?: return null
        val device = newest.file.device
        return GroupSessionStale(device.deviceId, device.name, heardAt(device, newest.receivedAt))
    }

    private val newestHeardFirst: Comparator<ReceivedSyncFile> =
        compareByDescending<ReceivedSyncFile> { heardAt(it.file.device, it.receivedAt) }.thenBy { it.file.device.deviceId.toString().lowercase() }

    private fun phaseStillOn(running: SyncRunning, now: Instant): Boolean =
        running.phaseEndsAt == null || running.awaySince != null || now.isBefore(running.phaseEndsAt)

    // While away the timer is paused, so the session ends that much later than the file's end says; a future awaySince adds nothing.
    private fun awayGap(running: SyncRunning, now: Instant): Duration {
        val since = running.awaySince
        return if (since != null && now.isAfter(since)) Duration.between(since, now) else Duration.ZERO
    }
}
