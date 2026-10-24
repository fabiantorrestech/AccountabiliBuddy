package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.FocusDayCombine
import com.accountabilibuddy.shared.DataSource
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.PcState
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.shared.FocusPart
import com.accountabilibuddy.shared.RunningSession
import com.accountabilibuddy.shared.SnapshotInput
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * What the phone knows, as the watch snapshot's input (ADR 21): today's plan, the PC's focus day and its session. While
 * the link is idle (the app isn't on screen), the PC's last state ([lastState], at [lastStateAt]) says what runs.
 */
object PhoneSnapshot {
    fun input(
        state: CompanionState,
        status: LinkStatus,
        linked: Boolean,
        pcName: String?,
        now: Instant,
        zone: ZoneId,
        wifiAllowed: Boolean = true,
        lastState: PcState? = null,
        lastStateAt: Instant? = null,
        relayAllowed: Boolean = true,
        /** The other devices' names and sync files on the phone (ADR 22): their focus joins today's, tagged. */
        received: List<Pair<String, SyncFile>> = emptyList(),
    ): SnapshotInput {
        val connected = status as? LinkStatus.Connected
        val pcConnected = connected?.state != null
        val today = now.atZone(zone).toLocalDate()
        val others = received.filter { (name, file) -> !(file.device.kind == SyncDeviceCard.KIND_PC && name == pcName) }
        return SnapshotInput(
            now = now,
            zone = zone,
            linked = linked,
            pcName = pcName,
            parts = todayParts(state.document.dayPlan, today, zone),
            // The linked PC's live day wins over its own sync file; the other devices' sessions join it.
            focusDay = if (received.isEmpty()) state.focusDay else FocusDayCombine.combine(state.focusDay, pcName, received, today, zone, now),
            focusDayAt = state.focusDayAt,
            sources = listOfNotNull(pcName?.let { name -> state.focusDayAt?.let { DataSource(name, it) } }) +
                others.map { (name, file) -> DataSource(name, file.device.writtenAt) },
            running = running(connected?.state ?: lastState)?.takeUnless { !pcConnected && newerFileRunning(received, lastStateAt, now) != null }
                ?: newerFileRunning(received, if (pcConnected) now else lastStateAt, now),
            pcConnected = pcConnected,
            wifiAllowed = wifiAllowed,
            runningAt = if (pcConnected) now else lastStateAt,
            relayAllowed = relayAllowed,
        )
    }

    /** The day plan's parts on [date] (midnight to midnight), as the PC would send them. */
    fun todayParts(plan: DayPlan, date: LocalDate, zone: ZoneId): List<FocusPart> {
        val start = date.atStartOfDay()
        return plan.partsIn(LocalWindow(start, start.plusDays(1))).map { part ->
            val kind = when (part.category.behavior) {
                CategoryBehavior.Watch, CategoryBehavior.Comms -> "watch"
                CategoryBehavior.Sleep -> "sleep"
                CategoryBehavior.Rest -> "rest"
                CategoryBehavior.PreWork -> "prework"
            }
            FocusPart(part.name, part.category.name, part.category.color, kind,
                part.window.start.atZone(zone).toInstant(), part.window.end.atZone(zone).toInstant())
        }
    }

    /**
     * A PC's running session from its sync file (ADR 26) when that file is newer than [than] (the link's last state):
     * the newest PC file written in the last 12 hours whose phase hasn't been over for more than 10 minutes (a PC that
     * went off mid-session stops counting down). Its open session's checklist comes along.
     */
    fun newerFileRunning(received: List<Pair<String, SyncFile>>, than: Instant?, now: Instant): RunningSession? =
        received
            .map { it.second }
            .filter { it.device.kind == SyncDeviceCard.KIND_PC && it.running != null }
            .filter { than == null || it.device.writtenAt.isAfter(than) }
            .filter { java.time.Duration.between(it.device.writtenAt, now) <= java.time.Duration.ofHours(12) }
            .filter { file -> file.running!!.phaseEndsAt?.let { now.isBefore(it.plusSeconds(600)) } ?: true }
            .maxByOrNull { it.device.writtenAt }
            ?.let { file ->
                val running = file.running!!
                val tasks = file.sessions.lastOrNull { it.end == null }?.tasks.orEmpty()
                RunningSession(running.profile, running.phaseStartedAt, running.phaseEndsAt, running.isBreak).withTasks(tasks)
            }

    /** The PC's session when it's one that counts as focus (a scheduled block isn't); breaks belong to it. */
    fun running(state: PcState?): RunningSession? {
        if (state == null || !state.isActive || state.isScheduledBlock) {
            return null
        }

        val startedAt = state.phaseStartedAt?.toInstant() ?: return null
        return RunningSession(state.profileName, startedAt, state.phaseEndsAt?.toInstant(), state.isBreak).withTasks(state.tasks, state.currentTask)
    }
}
