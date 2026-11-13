package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import java.time.Duration
import java.time.OffsetDateTime

/**
 * Deep work's weekly target and "Track time only" between devices (ADR 79, GS4; the twin of the PC's `GoalsService.Received`,
 * `ApplyPendingGatedAsync`, `CancelDeepWorkPendingAsync` and the local `NewStamp`), on the phone's [GoalDeepWorkWeekly]:
 * - **Out:** the weekly target in EFFECTIVE use (never a waiting one) with its own stamp ([targets]); the target the phone holds for Track time only is the PC's.
 * - **In** ([received]): a stamp that is not newer than the phone's own is ignored (a stale file); the same values take the newer stamp (a
 *   waiting change received earlier goes); higher, new or "Track time only" turning off applies at once, also during a session; lower, removed
 *   or "Track time only" turning on waits THIS phone's looser-change delay from the receipt (the trusted clock), is held while a focus session the
 *   user started runs (outside ProductaBuddy mode, its wait counted from the session's end) and lands with the sender's stamp, so nothing echoes.
 *   ProductaBuddy mode skips the delay and the session rule; the work-hours rule does not exist on the phone ([Env.lowerWaitsNow] false).
 * - **Cancel** of a received change stamps the phone's own value again ([cancel]), which the sender takes as a raise.
 * - **Local changes** ([setLocal], [setTrackTimeOnlyLocal]) are stamped now by this phone, one tick after every stamp they replace.
 */
object GoalDeepWorkWeeklySync {
    /** The facts the rules read, taken from the trusted clock at the moment of a change. */
    data class Env(
        val productaBuddy: Boolean,
        val sessionRunning: Boolean,
        val now: OffsetDateTime,
        val looserDelay: Duration,
        val lowerWaitsNow: Boolean = false,
    )

    private const val NO_DAILY_BASIS = 1.0

    /** The stamp of the weekly target in effect: the held one, else the stored Deep work record's own part stamp. */
    fun ownStamp(held: GoalDeepWorkWeekly, recordStamp: FieldStamp): FieldStamp = held.stamp ?: recordStamp

    /** What Deep work's record carries out: the weekly target in effect and Track time only, under their own stamp. */
    fun targets(held: GoalDeepWorkWeekly, recordStamp: FieldStamp): GoalDeepWorkTargets =
        GoalDeepWorkTargets(held.effectiveMinutes?.toDouble(), held.trackTimeOnly, ownStamp(held, recordStamp))

    /** A change made here: now by [by], after every stamp it replaces (a faster clock's received stamp included). */
    fun newStamp(held: GoalDeepWorkWeekly, recordStamp: FieldStamp, now: OffsetDateTime, by: String): FieldStamp =
        FieldStamp(FieldStamps.next(now, ownStamp(held, recordStamp).at, held.pendingStamp?.at), by)

    /** The merge's order for two stamps: the later time, then the device id (text). */
    fun isNewer(candidate: FieldStamp, than: FieldStamp): Boolean {
        val byTime = DotNetTime.compare(candidate.at, than.at)
        return if (byTime != 0) byTime > 0 else candidate.by.compareTo(than.by) > 0
    }

    fun sameStamp(a: FieldStamp?, b: FieldStamp?): Boolean =
        if (a == null || b == null) a == null && b == null else DotNetTime.compare(a.at, b.at) == 0 && a.by == b.by

    /**
     * Another device's weekly target and Track time only taken in against the ones in effect here ([own]: their stamp).
     * [pcDailyMinutes] is the PC's daily target, what Track time only would take away (null: unknown, treated as a target that exists).
     */
    fun received(held: GoalDeepWorkWeekly, received: GoalDeepWorkTargets, own: FieldStamp, env: Env, pcDailyMinutes: Double?): GoalDeepWorkWeekly {
        val settings = if (held.stamp == null) held.copy(stamp = own) else held
        if (sameStamp(received.stamp, own) || !isNewer(received.stamp, own)) {
            // The same, or older than what is in effect here (a stale file, say 600 after a raise to 900): nothing to take.
            return settings
        }

        val wanted: Int? = if (received.trackTimeOnly || received.weeklyMinutes == null) null else Math.max(1, Math.rint(received.weeklyMinutes).toInt())
        val sameInEffect = received.trackTimeOnly == settings.trackTimeOnly && (settings.trackTimeOnly || wanted == settings.minutes)
        if (sameInEffect) {
            // The same values: their newer stamp is taken; a change received earlier and still waiting is older than it and goes.
            return if (settings.pendingStamp == null) settings.copy(stamp = received.stamp)
            else settings.copy(stamp = received.stamp, pending = null, pendingTrackTimeOnly = null, pendingStamp = null, pendingAfterSession = false)
        }

        val looser = received.trackTimeOnly ||
            (!settings.trackTimeOnly && GoalLowering.isLooser(GoalDirection.AtLeast, settings.minutes?.toDouble(), wanted?.toDouble()))
        if (!looser) {
            // Higher, new, or "Track time only" turning off: stricter, at once (also during a session); any waiting change goes.
            return settings.copy(
                minutes = wanted, trackTimeOnly = false, stamp = received.stamp,
                pending = null, pendingTrackTimeOnly = null, pendingStamp = null, pendingAfterSession = false,
            )
        }

        val running = if (received.trackTimeOnly) settings.pendingTrackTimeOnly else settings.pending
        if (running != null && sameStamp(settings.pendingStamp, received.stamp) && sameAmount(running.amount, wanted?.toDouble())) {
            // Already waiting for this very change: its time stays (the same file read again).
            return settings
        }

        val pending: GoalPendingChange
        var afterSession = false
        if (!env.productaBuddy && env.sessionRunning) {
            // Held while the session runs; its wait counts from the session's end ([settle]).
            pending = GoalPendingChange(wanted?.toDouble(), env.now.plus(env.looserDelay))
            afterSession = true
        } else {
            val current = if (received.trackTimeOnly) GoalTarget(GoalPeriod.Day, pcDailyMinutes ?: NO_DAILY_BASIS, GoalUnit.Minutes)
            else GoalTarget(GoalPeriod.Week, settings.minutes?.toDouble(), GoalUnit.Minutes)
            val decision = GoalLowering.decide(deepWork(env.now), current, wanted?.toDouble(), env.productaBuddy, env.lowerWaitsNow, env.now, env.looserDelay)
            if (decision.outcome != GoalChangeOutcome.Wait) {
                val applied = if (received.trackTimeOnly) trackingTimeOnly(settings) else settings.copy(minutes = wanted, pending = null, pendingTrackTimeOnly = null)
                return applied.copy(stamp = received.stamp, pendingStamp = null, pendingAfterSession = false)
            }

            pending = decision.pending(wanted?.toDouble())!!
        }

        return if (received.trackTimeOnly) {
            settings.copy(pendingTrackTimeOnly = pending, pending = null, pendingStamp = received.stamp, pendingAfterSession = afterSession)
        } else {
            settings.copy(pending = pending, pendingTrackTimeOnly = null, pendingStamp = received.stamp, pendingAfterSession = afterSession)
        }
    }

    /**
     * Waiting changes whose time has come apply ([GoalLowering.canApply]): a session-held one counts its wait from the session's end, a
     * ProductaBuddy-mode one seen in AccountabiliBuddy mode restarts its full delay; a received change lands with its sender's stamp, one
     * made here is stamped as it lands.
     */
    fun settle(held: GoalDeepWorkWeekly, recordStamp: FieldStamp, env: Env, by: String): GoalDeepWorkWeekly {
        if (!held.hasPending) return tidy(held)
        var settings = held
        if (settings.pendingAfterSession && (env.productaBuddy || !env.sessionRunning)) {
            // A looser value received during a session waits its delay from the session's end (none in ProductaBuddy mode).
            val from = env.now.plus(env.looserDelay)
            fun rebased(pending: GoalPendingChange?): GoalPendingChange? =
                if (pending == null || env.productaBuddy || !pending.appliesAt.toInstant().isBefore(from.toInstant())) pending else pending.copy(appliesAt = from)
            settings = settings.copy(pending = rebased(settings.pending), pendingTrackTimeOnly = rebased(settings.pendingTrackTimeOnly), pendingAfterSession = false)
        }

        if (!env.productaBuddy) {
            fun restarted(pending: GoalPendingChange?): GoalPendingChange? = pending?.let { GoalLowering.intoAccountabiliBuddy(it, env.now, env.looserDelay) }
            settings = settings.copy(pending = restarted(settings.pending), pendingTrackTimeOnly = restarted(settings.pendingTrackTimeOnly))
        }

        fun ready(pending: GoalPendingChange?) =
            pending != null && GoalLowering.canApply(pending, env.now, env.sessionRunning, env.productaBuddy, env.lowerWaitsNow)
        if (ready(settings.pendingTrackTimeOnly)) {
            val stamp = settings.pendingStamp ?: newStamp(settings, recordStamp, env.now, by)
            settings = trackingTimeOnly(settings).copy(stamp = stamp, pendingStamp = null)
        }

        if (ready(settings.pending)) {
            val stamp = settings.pendingStamp ?: newStamp(settings, recordStamp, env.now, by)
            settings = settings.copy(minutes = settings.pending!!.amount?.toInt(), pending = null, stamp = stamp, pendingStamp = null)
        }

        return tidy(settings)
    }

    /**
     * Cancel on a waiting change. A change received from another device, cancelled here, stamps this phone's value again (newer than the
     * received one), so the other device takes it back as a raise.
     */
    fun cancel(held: GoalDeepWorkWeekly, recordStamp: FieldStamp, now: OffsetDateTime, by: String): GoalDeepWorkWeekly {
        val cancelled = held.copy(pending = null, pendingTrackTimeOnly = null)
        return tidy(if (held.pendingStamp != null) cancelled.copy(stamp = newStamp(held, recordStamp, now, by), pendingStamp = null) else cancelled)
    }

    /**
     * The user (or a backup) asks for [minutes] (null = none) here: the looser-change rule of [GoalDeepWorkWeeklyRules.set], a raise
     * stamped now; a weekly target ends "Track time only" first (stricter, at once).
     */
    fun setLocal(held: GoalDeepWorkWeekly, minutes: Int?, recordStamp: FieldStamp, env: Env, by: String): GoalDeepWorkWeeklyChange {
        var state = held
        val wanted = minutes?.coerceIn(GoalDeepWorkWeeklyRules.MIN_MINUTES, GoalDeepWorkWeeklyRules.MAX_MINUTES)
        if (wanted != null && (state.trackTimeOnly || state.pendingTrackTimeOnly != null)) {
            state = state.copy(
                trackTimeOnly = false, pendingTrackTimeOnly = null,
                stamp = if (state.trackTimeOnly) newStamp(state, recordStamp, env.now, by) else state.stamp,
            )
        }

        val change = GoalDeepWorkWeeklyRules.set(state, minutes, env.productaBuddy, env.sessionRunning, env.lowerWaitsNow, env.now, env.looserDelay)
        val next = when (change.result) {
            GoalDeepWorkWeeklyResult.Applied -> change.state.copy(stamp = newStamp(state, recordStamp, env.now, by))
            GoalDeepWorkWeeklyResult.Waiting ->
                if (change.state.pending != state.pending) change.state.copy(pendingStamp = null, pendingAfterSession = false) else change.state
            else -> change.state
        }
        return GoalDeepWorkWeeklyChange(tidy(next), change.result)
    }

    /**
     * "Track time only" asked for here (only a goals backup does): off is stricter, at once; on is a lowering that waits like any other.
     * [pcDailyMinutes]: the PC's daily target, what it would take away.
     */
    fun setTrackTimeOnlyLocal(held: GoalDeepWorkWeekly, on: Boolean, pcDailyMinutes: Double?, recordStamp: FieldStamp, env: Env, by: String): GoalDeepWorkWeeklyChange {
        if (!on) {
            if (!held.trackTimeOnly && held.pendingTrackTimeOnly == null) return GoalDeepWorkWeeklyChange(held, GoalDeepWorkWeeklyResult.Unchanged)
            val next = held.copy(
                trackTimeOnly = false, pendingTrackTimeOnly = null,
                stamp = if (held.trackTimeOnly) newStamp(held, recordStamp, env.now, by) else held.stamp,
            )
            return GoalDeepWorkWeeklyChange(tidy(next), GoalDeepWorkWeeklyResult.Applied)
        }

        if (held.trackTimeOnly) return GoalDeepWorkWeeklyChange(held, GoalDeepWorkWeeklyResult.Unchanged)
        val current = GoalTarget(GoalPeriod.Day, pcDailyMinutes ?: NO_DAILY_BASIS, GoalUnit.Minutes)
        val decision = GoalLowering.decide(deepWork(env.now), current, null, env.productaBuddy, env.lowerWaitsNow, env.now, env.looserDelay)
        if (decision.outcome == GoalChangeOutcome.ApplyNow) {
            val next = trackingTimeOnly(held).copy(stamp = newStamp(held, recordStamp, env.now, by))
            return GoalDeepWorkWeeklyChange(tidy(next), GoalDeepWorkWeeklyResult.Applied)
        }

        if (!env.productaBuddy && env.sessionRunning) return GoalDeepWorkWeeklyChange(held, GoalDeepWorkWeeklyResult.RefusedDuringSession)
        val running = held.pendingTrackTimeOnly
        if (running != null && running.amount == null) return GoalDeepWorkWeeklyChange(held, GoalDeepWorkWeeklyResult.Waiting)
        return GoalDeepWorkWeeklyChange(
            held.copy(pendingTrackTimeOnly = decision.pending(null), pendingStamp = null, pendingAfterSession = false), GoalDeepWorkWeeklyResult.Waiting,
        )
    }

    /** A received stamp and a session hold go once no weekly or "Track time only" change waits. */
    fun tidy(state: GoalDeepWorkWeekly): GoalDeepWorkWeekly =
        if (state.pending == null && state.pendingTrackTimeOnly == null && (state.pendingStamp != null || state.pendingAfterSession)) {
            state.copy(pendingStamp = null, pendingAfterSession = false)
        } else {
            state
        }

    private fun trackingTimeOnly(settings: GoalDeepWorkWeekly): GoalDeepWorkWeekly =
        settings.copy(trackTimeOnly = true, minutes = null, pendingTrackTimeOnly = null, pending = null)

    private fun deepWork(now: OffsetDateTime): Goal = GoalDefaults.newDeepWork(GoalDefaults.WORK_AREA_ID, now)

    private fun sameAmount(a: Double?, b: Double?): Boolean = if (a == null || b == null) a == null && b == null else a == b
}
