package com.accountabilibuddy.companion.placement

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.shared.session.PhoneLockArea
import com.accountabilibuddy.shared.session.PhoneSessionLocks
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant

/**
 * The PC focus session the phone's placement is about: the newest PC sync file's running session (planning counts, a
 * break is flagged), else the phone's own test session (so the feature can be tried without a real session; a PC
 * ignores a placement for a session it doesn't have).
 *
 * PS2c (ADR 85, plan check B1/B9) turns this object into the phone's lock seam as well, with three doors, each for a different question:
 * - [current]: the session the PLACEMENT is about (PC, else test). Never the phone's own session (placement is the PC's feature: sensors, work use,
 *   the parked notice). Placement-only consumers use this.
 * - [locking]: is [area] locked right now? A session on another device or the test session locks everything (as ever); the phone's own session locks its
 *   scope by mode (AccountabiliBuddy everything, ProductaBuddy only what its profile turns on); both together: the stricter. Every lock consumer uses this.
 * - [anyRunning]: does any real or test session run, in either mode? The mode switch and the pause master switch ask this (plan check S4/S8).
 *
 * The own session is read from the phone session store's flag, never compared with the caller's clock (B1): a phone clock moved past the set end
 * releases nothing; only the controller's trusted tick ends a session.
 */
object PlacementSession {
    /** Two starts this close are the same session (the PC compares with the same tolerance). */
    private val SAME_WITHIN: Duration = Duration.ofMinutes(2)
    private val FILE_FRESH_FOR: Duration = Duration.ofHours(12)

    /**
     * @property start The session's start: the PC file's counted session when it lists one ([authoritative]), else the
     * planning stage's start, else the phase's start.
     * @property onBreak On a break: no sensors run then.
     * @property test The phone's own test session (a real session on a PC always comes first).
     * @property endsAt When the running phase ends (the PC's set end, the test's end); null: no set end, or stepped away (the end moves on return).
     * @property own The phone's own session is part of it (only [locking] and [anyRunning] ever say so; [current] never does).
     */
    data class Info(val start: Instant, val authoritative: Boolean, val onBreak: Boolean, val endsAt: Instant? = null, val test: Boolean = false, val own: Boolean = false)

    fun same(a: Instant, b: Instant): Boolean = Duration.between(a, b).abs() <= SAME_WITHIN

    /**
     * @param ignoreOff true for the locks that must hold while the PC is off mid-session (device removal, restore, turning a guard off):
     * the session resumes when the PC is back, so they stay locked. The guards themselves leave it false and let go (ADR 60).
     */
    fun current(app: CompanionApp, now: Instant, ignoreOff: Boolean = false): Info? = fromPc(app, now, ignoreOff) ?: fromTest(app, now)

    /** The phone's own running REAL session, or null (a test one is the test session's). Read from the store's flag: no clock. */
    fun ownRecord(app: CompanionApp): PhoneSessionRecord? = app.phoneSession.running.value?.takeIf { it.isRunning && !it.isTest }

    /** What the phone's own running session locks now (empty when none runs): AccountabiliBuddy everything, ProductaBuddy what its profile turns on (R11). */
    fun ownAreas(app: CompanionApp): Set<PhoneLockArea> {
        val record = ownRecord(app) ?: return emptySet()
        return PhoneSessionLocks.scope(EssentialsLaunch.mode(app), record, EssentialsLaunch.ownProfile(app).use)
    }

    /** The session on another device (a PC's, always "ignoreOff": ADR 60's release is for the guards, not the locks) or the phone's test session. */
    private fun otherLocking(app: CompanionApp, now: Instant): Info? = fromPc(app, now, ignoreOff = true) ?: fromTest(app, now)

    /** Every area locked now: a PC or test session locks all, the own session its scope, both the stricter ([PhoneSessionLocks.locked]). */
    fun lockedAreas(app: CompanionApp, now: Instant): Set<PhoneLockArea> =
        PhoneSessionLocks.locked(otherLocking(app, now) != null, ownAreas(app))

    /**
     * The session that locks [area] now, or null when it is not locked. The lock sentence's end is the later of the sessions' ends (a lock holds until the
     * last locking session is over; no set end is the latest).
     */
    fun locking(app: CompanionApp, now: Instant, area: PhoneLockArea): Info? {
        val other = otherLocking(app, now)
        if (!PhoneSessionLocks.locks(area, other != null, ownAreas(app))) return null
        val own = ownRecord(app)?.takeIf { area in ownAreas(app) }?.let {
            Info(it.startedAt, authoritative = true, onBreak = false, endsAt = it.effectiveEnd(now), own = true)
        }
        if (other == null) return own
        if (own == null) return other
        return Info(minOf(other.start, own.start), authoritative = true, onBreak = false, endsAt = PhoneSessionLocks.laterEnd(other.endsAt, own.endsAt), own = true)
    }

    /** Any session runs, the phone's own in either mode too: the mode switch's and the pause master switch's gate (plan check S4/S8). */
    fun anyRunning(app: CompanionApp, now: Instant): Boolean = PhoneSessionLocks.anyRunning(otherLocking(app, now) != null, ownRecord(app) != null)

    private fun fromTest(app: CompanionApp, now: Instant): Info? =
        app.testSession.running(now)?.let { (start, end) -> Info(start, authoritative = true, onBreak = false, endsAt = end, test = true) }

    private fun fromPc(app: CompanionApp, now: Instant, ignoreOff: Boolean): Info? {
        val file = app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && Duration.between(it.file.device.writtenAt, now) <= FILE_FRESH_FOR }
            .filter { it.file.running?.isLiveAt(now, Duration.ZERO) == true }
            // ADR 60: a PC that went off holds the phone for 10 minutes, then lets go until it's back.
            .filter { ignoreOff || com.accountabilibuddy.companion.sync.PcFileControl.guardsHold(it.file, now) }
            // A stepped-away session holds the guards (its end moves on return) only while its file is fresh: a PC that went away
            // for real stops holding the phone after the no-set-end limit (ADR 45).
            .filter { it.file.running?.isAwayOverdue(it.file.device.writtenAt, now) != true }
            .maxByOrNull { it.file.device.writtenAt } ?: return null
        val running = file.file.running!!
        // The session's own start (ADR 85, `running.sessionStartedAt`) in preference to the open piece's start: a session is cut at every away
        // stretch, so the open piece's start moves, and per-session state (work-use uses, the placement record) must not reset with it.
        // An older file has no such start: the open piece's, as before.
        val counted = running.sessionStartedAt ?: file.file.sessions.lastOrNull { it.end == null && !it.added }?.start
        return Info(counted ?: running.planningStartedAt ?: running.phaseStartedAt, authoritative = counted != null, onBreak = running.isBreak, endsAt = running.phaseEndsAt.takeIf { running.awaySince == null })
    }
}
