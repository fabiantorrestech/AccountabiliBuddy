package com.accountabilibuddy.shared.goals

import java.nio.charset.StandardCharsets
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID
import kotlin.math.floor

/**
 * Health Connect sleep as goal readings on the phone (round 2 R6b3, brief §6.1, plan R6.4): the sleep the phone already reads
 * (`SleepReader`, ADR 11; only once the user granted it) becomes, for each goal with a switched-on sleep source, one reading a day: the
 * minutes asleep in the night that ended that morning (a stretch belongs to the night of the day 12 hours before it ended, as the
 * phone's sleep week counts it, and to the day after that night; overlapping stretches count once; whole minutes). In memory only, like
 * steps: never stored with the goals, backed up, synced or sent to the watch. A hand edit of the day wins over the reading (the goals'
 * own counting rule).
 */
object GoalSleepReadings {
    /** [slept]: the sleep stretches as local start and end; [readAt]: when Health Connect was last read (the readings' stamp). */
    fun entries(goals: List<Goal>, slept: List<GoalWindow>, readAt: Instant?, zone: ZoneId): List<GoalEntry> {
        if (readAt == null || slept.isEmpty()) return emptyList()
        val counting = goals.filter { goal -> !goal.isDeepWork && goal.sources.any { it.type == GoalSourceType.HealthSleep && it.automatic } }
        if (counting.isEmpty()) return emptyList()
        val byDay = minutesByDay(slept)
        val stamp = OffsetDateTime.ofInstant(readAt, zone)
        return counting.flatMap { goal ->
            byDay.map { (day, minutes) ->
                val id = UUID.nameUUIDFromBytes("sleep:${goal.id}:$day".toByteArray(StandardCharsets.UTF_8))
                GoalEntry(id, goal.id, day, null, minutes, GoalEntrySource.HealthConnect, stamp)
            }
        }
    }

    /** Minutes asleep by the day each night ends on, whole minutes. */
    fun minutesByDay(slept: List<GoalWindow>): Map<LocalDate, Double> =
        slept.filter { it.end > it.start }
            .groupBy { it.end.minusHours(12).toLocalDate().plusDays(1) }
            .mapValues { (_, windows) -> floor(GoalTimeline.union(windows).sumOf { it.minutes } + 1e-9) }
            .filterValues { it > 0 }
            .toSortedMap()
}
