package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.OffsetDateTime

/** The PC's last state and when it came, kept so the watch still shows what runs (ADR 21). */
data class PcStateAt(val state: PcState, val at: java.time.Instant)

/** A profile the phone can start on the PC. */
data class PcProfile(val id: String, val name: String)

/**
 * The PC's session as it sent it (times are absolute, so the phone counts down by itself between updates).
 * [mode] is "manual", "pomodoro" or "scheduled", null when nothing runs; scheduled blocks block but aren't focus.
 */
data class PcState(
    val mode: String?,
    val phase: String,
    val countsAsFocus: Boolean,
    val profileId: String?,
    val profileName: String?,
    val phaseStartedAt: OffsetDateTime?,
    val phaseEndsAt: OffsetDateTime?,
    val profiles: List<PcProfile>,
    val countUpProfileId: String?,
    /** The running session's guided checklist (ADR 24); empty for none or an older PC. */
    val tasks: List<SessionTaskItem> = emptyList(),
    /** The task or subtask picked on the PC to show; null for none (the first open task shows). */
    val currentTask: String? = null,
    /** The PC's open come-backs (ADR 30); empty when it has none open, null from an older PC that doesn't send them. */
    val comebacks: List<com.accountabilibuddy.shared.sync.SyncComeback>? = null,
    /**
     * Set while the PC is in its planning stage (not focus time), from its sync file only: when it began and when its time is up.
     * [phaseStartedAt] and [phaseEndsAt] are then the focus phase it leads to. Null outside planning and for an older PC.
     */
    val planningStartedAt: OffsetDateTime? = null,
    val planningEndsAt: OffsetDateTime? = null,
) {
    val isPlanning: Boolean get() = planningEndsAt != null && !isBreak
    val isActive: Boolean get() = mode != null
    val isBreak: Boolean get() = phase == "shortBreak" || phase == "longBreak"
    val isScheduledBlock: Boolean get() = mode == "scheduled"
}
