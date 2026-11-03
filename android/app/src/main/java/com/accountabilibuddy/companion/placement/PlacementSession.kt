package com.accountabilibuddy.companion.placement

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant

/**
 * The PC focus session the phone's placement is about: the newest PC sync file's running session (planning counts, a
 * break is flagged), else the phone's own test session (so the feature can be tried without a real session; a PC
 * ignores a placement for a session it doesn't have).
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
     */
    data class Info(val start: Instant, val authoritative: Boolean, val onBreak: Boolean, val endsAt: Instant? = null, val test: Boolean = false)

    fun same(a: Instant, b: Instant): Boolean = Duration.between(a, b).abs() <= SAME_WITHIN

    fun current(app: CompanionApp, now: Instant): Info? = fromPc(app, now) ?: fromTest(app, now)

    private fun fromTest(app: CompanionApp, now: Instant): Info? =
        app.testSession.running(now)?.let { (start, end) -> Info(start, authoritative = true, onBreak = false, endsAt = end, test = true) }

    private fun fromPc(app: CompanionApp, now: Instant): Info? {
        val file = app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && Duration.between(it.file.device.writtenAt, now) <= FILE_FRESH_FOR }
            .filter { it.file.running?.isLiveAt(now, Duration.ZERO) == true }
            // ADR 60: a PC that went off holds the phone for 10 minutes, then lets go until it's back.
            .filter { com.accountabilibuddy.companion.sync.PcFileControl.guardsHold(it.file, now) }
            // A stepped-away session holds the guards (its end moves on return) only while its file is fresh: a PC that went away
            // for real stops holding the phone after the no-set-end limit (ADR 45).
            .filter { it.file.running?.isAwayOverdue(it.file.device.writtenAt, now) != true }
            .maxByOrNull { it.file.device.writtenAt } ?: return null
        val running = file.file.running!!
        val counted = file.file.sessions.lastOrNull { it.end == null && !it.added }?.start
        return Info(counted ?: running.planningStartedAt ?: running.phaseStartedAt, authoritative = counted != null, onBreak = running.isBreak, endsAt = running.phaseEndsAt.takeIf { running.awaySince == null })
    }
}
