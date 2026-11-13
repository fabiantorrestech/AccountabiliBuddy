package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The values the phone measures itself and writes as goal entries (ADR 79, GS4): Health Connect steps and sleep, one reading a day per
 * goal that counts them (the PC's `GoalDerivedWriter` writes the entries, [GoalDerivedWriter.changes]). Only while the source is on:
 * steps while "Track daily steps" is on (the caller passes no days otherwise), sleep once the user granted Health Connect sleep (no
 * stretches otherwise); a source that is off writes nothing and removes nothing. A real 0 is written for a measured day. Screen time is
 * ST3's, not here. A goal that counts both steps and sleep gets the higher of the two for a day (the entry id is goal + source + device + day).
 */
object GoalPhoneDerived {
    /** The readings and the days the read covered (so an entry the read no longer finds is removed, by its writer only). */
    data class Result(val readings: List<GoalDerivedReading>, val covered: List<GoalDerivedCover>)

    /**
     * @param stepDays the last Health Connect read's days with their counts (oldest first); empty when steps are off or nothing was read.
     * @param slept the sleep stretches the phone read (the last 21 days); empty without the permission or before the first read.
     * @param today the phone's local date (the sleep read covers the 21 days up to it).
     */
    fun readings(goals: List<Goal>, stepDays: List<Pair<LocalDate, Long>>, slept: List<GoalWindow>, today: LocalDate): Result {
        val live = goals.filter { it.isLive && !it.isDeepWork }
        val stepsGoals = if (stepDays.isEmpty()) emptyList() else live.filter { goal -> goal.sources.any { it.type == GoalSourceType.HealthSteps && it.automatic } }
        val sleepGoals = if (slept.isEmpty()) emptyList() else live.filter { goal -> goal.sources.any { it.type == GoalSourceType.HealthSleep && it.automatic } }
        val values = LinkedHashMap<Pair<java.util.UUID, LocalDate>, Double>()
        val covered = ArrayList<GoalDerivedCover>()

        if (stepsGoals.isNotEmpty()) {
            val first = stepDays.minOf { it.first }
            val last = stepDays.maxOf { it.first }
            for (goal in stepsGoals) {
                for ((day, count) in stepDays) values.merge(goal.id to day, count.toDouble(), ::maxOf)
                covered.add(GoalDerivedCover(goal.id, GoalEntrySource.HealthConnect, first, last))
            }
        }

        if (sleepGoals.isNotEmpty()) {
            val byDay = GoalSleepReadings.minutesByDay(slept)
            for (goal in sleepGoals) {
                for ((day, minutes) in byDay) values.merge(goal.id to day, minutes, ::maxOf)
                covered.add(GoalDerivedCover(goal.id, GoalEntrySource.HealthConnect, today.minusDays(SLEEP_DAYS - 1L), today))
            }
        }

        val readings = values.map { (key, value) -> GoalDerivedReading(key.first, GoalEntrySource.HealthConnect, key.second, value) }
        return Result(readings, mergedCovers(covered))
    }

    // One goal with both a steps and a sleep source covers the wider range once.
    private fun mergedCovers(covers: List<GoalDerivedCover>): List<GoalDerivedCover> =
        covers.groupBy { it.goalId to it.source }.map { (_, group) -> GoalDerivedCover(group[0].goalId, group[0].source, group.minOf { it.from }, group.maxOf { it.to }) }

    /** The days a sleep read covers. */
    const val SLEEP_DAYS = 21
}
