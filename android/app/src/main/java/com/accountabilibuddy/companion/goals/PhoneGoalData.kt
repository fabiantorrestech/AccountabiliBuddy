package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.shared.goals.GoalSleepReadings
import com.accountabilibuddy.shared.goals.GoalWindow
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalFocusAttribution
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalSyncedPiece
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID
import kotlinx.serialization.json.JsonArray

/**
 * What the phone counts goals from (round 2 R6; plan §3b 5, 7): nothing new is read or sent.
 * - **Focus:** each received device's sessions as the sync file JSON carries them (the phone's kept copy of it, written back out by
 *   [SyncFileJson]), read through [GoalJson.readPiece] into [GoalSyncedPiece]s (Q391) and attributed by [GoalFocusAttribution.fromSynced],
 *   each device on its own, removed devices' kept copies included (as Stats counts them). The phone runs no sessions of its own.
 * - **Hand entries** from the store.
 * - **Steps:** the phone's existing daily steps, as readings of goals with a steps source, only while "Track daily steps" is on
 *   (which is what gates READ_STEPS); no new read or permission.
 * - **Sleep:** the sleep the phone already read from Health Connect (READ_SLEEP, granted by the user for the sleep pages), as one reading
 *   a day for goals with a sleep source ([GoalSleepReadings]); in memory only. Screen time isn't counted yet.
 * - #tag rules and calendar time are the PC's ([GoalSourceData.forPhone]): those sources say "Measured on your PC".
 */
object PhoneGoalData {
    fun build(
        files: List<SyncFile>,
        goals: List<Goal>,
        entries: List<GoalEntry>,
        steps: StepsState?,
        now: Instant,
        zone: ZoneId,
        slept: List<GoalWindow> = emptyList(),
    ): GoalSourceData {
        val focus = files.filter { it.device.kind != SyncDeviceCard.KIND_WATCH }.flatMap { file ->
            GoalFocusAttribution.fromSynced(pieces(file), now, zone)
        }
        val sleep = GoalSleepReadings.entries(goals, slept, if (slept.isEmpty()) null else now, zone)
        // ADR 79 (GS4): the phone writes its steps and sleep as stored entries ([PhoneGoalDerived]); a reading for a goal and day that already
        // has a stored Health Connect entry is left out, so nothing counts twice. The in-memory readings stay for a goal not saved yet (a draft).
        val stored = entries.filter { it.source == GoalEntrySource.HealthConnect }.map { it.goalId to it.day }.toSet()
        val fresh = (stepReadings(goals, steps, zone) + sleep).filter { (it.goalId to it.day) !in stored }
        return GoalSourceData.forPhone(focus, entries + fresh, zone)
    }

    /**
     * The sleep the phone already read from Health Connect (`SleepReader`, only once the user granted it; the last 21 days, in memory),
     * as goal windows for sleep sources (round 2 R6b3). Empty without the permission or before the first read.
     */
    fun slept(app: CompanionApp): List<GoalWindow> = app.store.state.value.slept.map { GoalWindow(it.start, it.end) }

    /** A device's sessions through the goals' own reader; a piece that can't be read is left out. */
    fun pieces(file: SyncFile): List<GoalSyncedPiece> {
        val sessions = SyncFileJson.write(file)["sessions"] as? JsonArray ?: return emptyList()
        return sessions.mapNotNull { runCatching { GoalJson.readPiece(it) }.getOrNull() }
    }

    // The day counts as Health Connect readings of each goal that counts steps (switched on), stamped when they were read. In memory
    // only: never stored with the goals, never backed up. Each reading's id is fixed by its goal and day.
    private fun stepReadings(goals: List<Goal>, steps: StepsState?, zone: ZoneId): List<GoalEntry> {
        if (steps == null || !steps.enabled) return emptyList()
        val readAt = steps.readAt ?: return emptyList()
        val stamp = OffsetDateTime.ofInstant(readAt, zone)
        val counting = goals.filter { goal -> !goal.isDeepWork && goal.sources.any { it.type == GoalSourceType.HealthSteps && it.automatic } }
        return counting.flatMap { goal ->
            steps.days.map { day ->
                val id = UUID.nameUUIDFromBytes("steps:${goal.id}:${day.date}".toByteArray(StandardCharsets.UTF_8))
                GoalEntry(id, goal.id, day.date, null, day.count.toDouble(), GoalEntrySource.HealthConnect, stamp)
            }
        }
    }
}
