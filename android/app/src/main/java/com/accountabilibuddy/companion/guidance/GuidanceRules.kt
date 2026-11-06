package com.accountabilibuddy.companion.guidance

import com.accountabilibuddy.shared.sync.SyncGuidance
import java.time.Duration
import java.time.Instant

/** How the app helps (ADR 68), one choice for every linked device: shown as AccountabiliBuddy ([Guarding]) and ProductaBuddy ([Companion]). */
enum class GuidanceMode {
    /** Guards: the work card, the work-start nudge, Pre-Work, the parked notice, bedtime reminders and the night's lock. */
    Guarding,

    /** The companion only: sessions, the day, tasks, stats, steps, sounds and Curbox's blocks; nothing nudges or locks otherwise. */
    Companion,
}

/**
 * A switch to [GuidanceMode.Companion] waiting out the looser-change delay: [editedAt] when it was asked (on any device), [applyAt]
 * when it may apply. [duringSession]: "now" during a focus session, paid with what ending it early costs (its wait and a reason), so
 * it applies with the session still running; never travels (a device that receives the mode holds it until its session ends).
 */
data class GuidancePending(val editedAt: Instant, val applyAt: Instant, val duringSession: Boolean = false)

/**
 * The guidance mode on this phone (preferences "guidance"). [editedAt] is when the mode in effect was chosen on any device; null for a
 * fresh install's ProductaBuddy nobody chose (never sent, gives way to any received mode); [GuidanceState.UPGRADE_STAMP] for an upgraded
 * install's AccountabiliBuddy (sent, older than any real choice).
 */
data class GuidanceState(
    val mode: GuidanceMode = GuidanceMode.Guarding,
    val editedAt: Instant? = null,
    val pending: GuidancePending? = null,
) {
    /** The mode in effect was chosen by someone (here or on another device), not a starting default. */
    val isChosen: Boolean get() = editedAt?.isAfter(UPGRADE_STAMP) == true

    companion object {
        /** The edit time of an upgraded install's AccountabiliBuddy: before any real choice, but sent, so a fresh device joining follows it. */
        val UPGRADE_STAMP: Instant = Instant.parse("2000-01-01T00:00:00Z")

        /** An install that predates the mode: guarding, as it always was. */
        val UPGRADED = GuidanceState(GuidanceMode.Guarding, UPGRADE_STAMP)

        /** A fresh install's default: ProductaBuddy, not yet chosen. */
        val FRESH_INSTALL = GuidanceState(GuidanceMode.Companion)
    }
}

/**
 * The mode's rules, the same as the PC's `GuidanceRules` (plain Kotlin so the checks run them). Switching to Companion is looser: it
 * waits the delay, is refused during a session and applies only with none running. Switching to Guarding is stricter: at once.
 * Between devices the newest edit wins; a received Companion waits this phone's delay from its edit time.
 */
object GuidanceRules {
    // A stamp may run at most this far ahead of this phone's clock (the rule other synced stamps follow).
    private val MAX_STAMP_AHEAD: Duration = Duration.ofMinutes(2)

    /** Asks for Companion: it waits [delay]. Null when refused (a session runs). */
    fun requestCompanion(state: GuidanceState, now: Instant, delay: Duration, sessionActive: Boolean): GuidanceState? {
        if (state.mode == GuidanceMode.Companion || state.pending != null) return state
        return if (sessionActive) null else state.copy(pending = GuidancePending(now, now.plus(delay)))
    }

    /** During a session, "after this session ends": the delay, then no session running (it changes nothing until then). */
    fun requestCompanionAfterSession(state: GuidanceState, now: Instant, delay: Duration): GuidanceState {
        if (state.mode == GuidanceMode.Companion || state.pending != null) return state
        return state.copy(pending = GuidancePending(now, now.plus(delay)))
    }

    /** During a session, "now", once what ending it early costs was paid: applies after [delay] with the session still running. */
    fun requestCompanionNow(state: GuidanceState, now: Instant, delay: Duration): GuidanceState {
        if (state.mode == GuidanceMode.Companion) return state
        return state.copy(pending = GuidancePending(now, now.plus(delay), duringSession = true))
    }

    /** "Keep" on the setup page: the starting default in effect becomes a choice, with no wait (nothing changes here); then it is sent. */
    fun confirmDefault(state: GuidanceState, now: Instant): GuidanceState =
        if (state.isChosen || state.pending != null) state else state.copy(editedAt = now)

    /** Guarding at once, cancelling any waiting switch (stricter, so never refused). */
    fun chooseGuarding(now: Instant): GuidanceState = GuidanceState(GuidanceMode.Guarding, now, null)

    /** Applies a waiting switch once its time has come and no session runs; one paid for during the session applies anyway. */
    fun applyIfDue(state: GuidanceState, now: Instant, sessionActive: Boolean): GuidanceState {
        val pending = state.pending ?: return state
        if ((sessionActive && !pending.duringSession) || now.isBefore(pending.applyAt)) return state
        return GuidanceState(GuidanceMode.Companion, pending.editedAt, null)
    }

    /** Takes another device's choice when it is newer than this phone's and than a switch waiting here. */
    fun receive(state: GuidanceState, received: SyncGuidance, now: Instant, delay: Duration): GuidanceState {
        val mode = when (received.mode) {
            SyncGuidance.GUARDING -> GuidanceMode.Guarding
            SyncGuidance.COMPANION -> GuidanceMode.Companion
            else -> return state
        }
        if (received.editedAt.isAfter(now.plus(MAX_STAMP_AHEAD))) return state
        state.editedAt?.let { ours -> if (!ours.isBefore(received.editedAt)) return state }
        state.pending?.let { waiting -> if (!waiting.editedAt.isBefore(received.editedAt)) return state }
        if (mode == GuidanceMode.Guarding) return GuidanceState(GuidanceMode.Guarding, received.editedAt, null)
        if (state.mode == GuidanceMode.Companion) return state.copy(editedAt = received.editedAt, pending = null)
        return state.copy(pending = GuidancePending(received.editedAt, received.editedAt.plus(delay)))
    }

    /** What goes in the sync file: the chosen mode, or null for a default nobody chose. */
    fun forSync(state: GuidanceState): SyncGuidance? {
        val editedAt = state.editedAt ?: return null
        return SyncGuidance(if (state.mode == GuidanceMode.Companion) SyncGuidance.COMPANION else SyncGuidance.GUARDING, editedAt)
    }
}
