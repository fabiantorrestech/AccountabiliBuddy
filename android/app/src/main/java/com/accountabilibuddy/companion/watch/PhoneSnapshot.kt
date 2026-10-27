package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.FocusDayCombine
import com.accountabilibuddy.shared.DataSource
import com.accountabilibuddy.companion.data.CompanionState
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
 * What the phone knows, as the watch snapshot's input (ADR 21): today's plan, the focus the devices' sync files carry
 * (ADR 22), and the session a PC's file says runs.
 */
object PhoneSnapshot {
    fun input(
        state: CompanionState,
        linked: Boolean,
        pcName: String?,
        now: Instant,
        zone: ZoneId,
        relayAllowed: Boolean = true,
        /** The devices' names and sync files on the phone (ADR 22): their focus makes today's, tagged with the device. */
        received: List<Pair<String, SyncFile>> = emptyList(),
    ): SnapshotInput {
        val today = now.atZone(zone).toLocalDate()
        val fileRunning = runningFromFiles(received, now)
        return SnapshotInput(
            now = now,
            zone = zone,
            linked = linked,
            pcName = pcName,
            parts = todayParts(state.document.dayPlan, today, zone),
            // The parts and planned minutes come from the imported plan (ADR 42).
            focusDay = FocusDayCombine.combine(received, today, zone, now, planParts = { date -> todayParts(state.document.dayPlan, date, zone) }),
            sources = received.map { (name, file) -> DataSource(name, file.device.writtenAt) },
            running = fileRunning?.first,
            runningAt = fileRunning?.second,
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
     * A PC's running session from its sync file (ADR 26) with the time that file was written: the newest PC file written
     * in the last 12 hours whose phase hasn't been over for more than 10 minutes (a PC that went off mid-session stops
     * counting down). Its open session's checklist comes along.
     */
    fun runningFromFiles(received: List<Pair<String, SyncFile>>, now: Instant): Pair<RunningSession, Instant>? =
        received
            .map { it.second }
            .filter { it.device.kind == SyncDeviceCard.KIND_PC && it.running != null }
            .filter { java.time.Duration.between(it.device.writtenAt, now) <= java.time.Duration.ofHours(12) }
            .filter { file -> file.running!!.isLiveAt(now, java.time.Duration.ofSeconds(600)) }
            .maxByOrNull { it.device.writtenAt }
            ?.let { file ->
                val running = file.running!!
                val tasks = file.sessions.lastOrNull { it.end == null }?.tasks.orEmpty()
                RunningSession(
                    running.profile, running.phaseStartedAt, running.phaseEndsAt, running.isBreak,
                    planningStartedAt = running.planningStartedAt, planningEndsAt = running.planningEndsAt,
                ).withTasks(tasks, running.currentTask) to file.device.writtenAt
            }
}
