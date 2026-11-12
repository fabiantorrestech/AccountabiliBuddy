package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.steps.StepsState
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
 *   (which is what gates READ_STEPS); no new read or permission. Sleep and screen time aren't counted yet.
 * - #tag rules and calendar time are the PC's ([GoalSourceData.forPhone]): those sources say "Measured on your PC".
 */
object PhoneGoalData {
    fun build(files: List<SyncFile>, goals: List<Goal>, entries: List<GoalEntry>, steps: StepsState?, now: Instant, zone: ZoneId): GoalSourceData {
        val focus = files.filter { it.device.kind != SyncDeviceCard.KIND_WATCH }.flatMap { file ->
            GoalFocusAttribution.fromSynced(pieces(file), now, zone)
        }
        return GoalSourceData.forPhone(focus, entries + stepReadings(goals, steps, zone), zone)
    }

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
