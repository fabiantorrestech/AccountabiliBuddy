package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID
import kotlin.math.floor

/**
 * Screen time counted on this device and written as this device's own goal entries (ADR 79, ST3; the phone's twin of Core's PC reader).
 * For each live goal with an automatic screen-time rule that counts on this device ([GoalAppMatching.countsOn]), a day's value is the
 * whole minutes of the device's usage rows that ANY entry of the goal's counted rules matches ([GoalAppMatching.matches], phone rules):
 * a row counts once however many entries or rules hit it. The other devices' values are not made here: the counting adds each chosen
 * device's entry per day ([GoalCounting], screen time is summed over the devices the rule counts on).
 *
 * Nothing private leaves: only a number per goal and day is written; the app names and packages in [rowsByDay] are never part of an
 * entry. A day with no rows is "no data" (no Usage access, or the records have expired), not a real zero: it is left out, so an
 * earlier value is never overwritten with a 0 and nothing is removed (a day with phone use that matched nothing is a real 0). Like
 * the steps (GS4) the entries are written with or without a group; with none they simply stay on the phone.
 */
object GoalScreenTimeDerived {
    /** The days read: today and the six before (Android keeps the usage records about that long). */
    const val DAYS = 7

    /** The goals this device counts screen time for. */
    fun counting(goals: List<Goal>, deviceId: UUID): List<Goal> =
        goals.filter { goal -> goal.isLive && !goal.isDeepWork && countedRules(goal, deviceId).isNotEmpty() }

    /** The whole minutes of [rows] that the goal's counted rules match; a row counts once. */
    fun minutes(goal: Goal, deviceId: UUID, rows: List<GoalAppRow>): Double {
        val rules = countedRules(goal, deviceId)
        val matched = rows.filter { row -> rules.any { rule -> rule.apps.orEmpty().any { GoalAppMatching.matches(it, row, GoalAppPlatform.Phone) } } }
        return floor(matched.sumOf { it.minutes } + 1e-9)
    }

    /**
     * The readings for the days that have rows. No covers: nothing is ever removed by a read (see the class text). With [today] given, only
     * today and yesterday are updated; an older day is written only when [held] has no live entry of this device for the goal and day yet
     * (a backfill), because Android's oldest records can come back partial and must never lower a stored value.
     */
    fun readings(
        goals: List<Goal>, deviceId: UUID, rowsByDay: Map<LocalDate, List<GoalAppRow>>,
        today: LocalDate? = null, held: List<GoalEntry> = emptyList(),
    ): GoalPhoneDerived.Result {
        val readings = ArrayList<GoalDerivedReading>()
        val have = held.filter { it.source == GoalEntrySource.ScreenTime && it.deviceId == deviceId && it.isLive }.map { it.goalId to it.day }.toSet()
        for (goal in counting(goals, deviceId)) {
            for ((day, rows) in rowsByDay.toSortedMap()) {
                if (rows.isEmpty()) continue
                if (today != null && day < today.minusDays(1) && (goal.id to day) in have) continue
                readings += GoalDerivedReading(goal.id, GoalEntrySource.ScreenTime, day, minutes(goal, deviceId, rows))
            }
        }

        return GoalPhoneDerived.Result(readings, emptyList())
    }

    /**
     * In-memory entries for a goal and day this device has no stored entry for yet (a goal not saved, or before the first write), so a
     * draft's "What it takes" counts them; never stored. [stored]: the goal entries held, of any device.
     */
    fun unstored(goals: List<Goal>, deviceId: UUID, rowsByDay: Map<LocalDate, List<GoalAppRow>>, stored: List<GoalEntry>, readAt: OffsetDateTime): List<GoalEntry> {
        val held = stored.filter { it.source == GoalEntrySource.ScreenTime && it.deviceId == deviceId }.map { it.goalId to it.day }.toSet()
        return readings(goals, deviceId, rowsByDay).readings
            .filter { (it.goalId to it.day) !in held }
            .map { GoalEntry(GoalDerivedIds.forEntry(it.goalId, GoalEntrySource.ScreenTime, deviceId, it.day), it.goalId, it.day, null, it.value, GoalEntrySource.ScreenTime, readAt, deviceId = deviceId) }
    }

    private fun countedRules(goal: Goal, deviceId: UUID): List<GoalSourceRule> =
        goal.sources.filter { it.type == GoalSourceType.ScreenTime && it.automatic && !it.apps.isNullOrEmpty() && GoalAppMatching.countsOn(it, deviceId) }
}
