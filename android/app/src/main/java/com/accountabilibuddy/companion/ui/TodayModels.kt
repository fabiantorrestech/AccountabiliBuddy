package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.companion.model.FixedLunch
import com.accountabilibuddy.companion.model.FlexibleBreak
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncLunch
import java.time.Instant
import java.util.UUID

/**
 * The controlled PC as Today needs it (ADR 42, 45): its name and file age, its session from the file ([state], null with no PC),
 * what keeps this phone from starting or ending things ([updateNeeded], [waiting]) and the running session's checklist.
 * [name] is null while no PC file has reached this phone.
 */
class TodayPc(
    val name: String?,
    val pcId: UUID?,
    val state: PcState?,
    val writtenAt: Instant?,
    val awaySince: Instant?,
    val updateNeeded: String?,
    val waiting: SyncAction?,
    val tasks: RunningTasks?,
) {
    val hasPc: Boolean get() = name != null
    val inSession: Boolean get() = state?.isActive == true
}

/** What the Today buttons do. The shell wires each to the existing action; the pane never decides anything itself. */
class TodayActions(
    /** Start on the controlled PC: profile id (null = its default), minutes, count up. */
    val onStartOnPc: (profileId: String?, minutes: Int?, countUp: Boolean) -> Unit,
    /** "End early…": asks the PC for its terms (wait and reason), then the end-early dialog opens. */
    val onEndEarly: () -> Unit,
    val onCancelWaiting: () -> Unit,
    val onImport: () -> Unit,
    val onOpenDevices: () -> Unit,
    val onOpenTasks: () -> Unit,
    val onAllowUsage: () -> Unit,
)

/** Today's lunch: what the plan says, whether it was taken, and what the buttons do. */
class TodayLunch(
    val workday: Boolean,
    val flex: FlexibleBreak?,
    val fixed: FixedLunch?,
    val taken: SyncLunch?,
    val lengthMinutes: Int,
    val onStart: () -> Unit,
    val onBack: () -> Unit,
    val onAdjust: (longer: Boolean) -> Unit,
)
