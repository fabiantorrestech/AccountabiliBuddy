package com.accountabilibuddy.shared.goals

import java.time.ZoneId

/**
 * An amount goal's readings, newest first, each marked with whether the chart uses it. Every value of a day is kept; the day's value
 * is the latest hand edit, else its first reading ([GoalCounting.decidingEntry], the one rule the numbers and the chart share). Only
 * what counts is listed: hand entries and readings from a source the goal has switched on.
 */
object GoalReadings {
    /** [entries]: the goal's live entries (any goal's: others are left out). Newest first: day, then time, then when it was made, then id. */
    fun of(goal: Goal, entries: List<GoalEntry>, zone: ZoneId): List<GoalReading> {
        val readable = GoalSources.countedFor(goal).filter { it.automatic }.mapNotNull { GoalCounting.entrySourceOf(it.type) }.toSet()
        val mine = entries.filter { it.goalId == goal.id && it.isLive && (it.source == GoalEntrySource.Hand || it.source in readable) }
        val deciding = mine.groupBy { it.day }.mapValues { (_, day) -> GoalCounting.decidingEntry(goal, day)?.id }
        val handDays = mine.filter { it.source == GoalEntrySource.Hand }.map { it.day }.toSet()
        return mine
            .map { entry ->
                val charted = deciding[entry.day] == entry.id
                val hand = entry.source == GoalEntrySource.Hand
                GoalReading(
                    entry = entry,
                    day = entry.day,
                    time = entry.time ?: entry.editedAt.atZoneSameInstant(zone).toLocalTime(),
                    value = entry.value,
                    source = sourceText(entry.source),
                    charted = charted,
                    note = when {
                        hand && charted -> "charted, your edit wins"
                        hand -> "not charted, a later edit wins"
                        charted -> "charted, first of the day"
                        entry.day in handDays -> "not charted, your edit wins"
                        else -> "not charted, an earlier reading is used"
                    },
                )
            }
            .sortedWith(
                compareByDescending<GoalReading> { it.day }
                    .thenByDescending { it.time }
                    .thenByDescending { it.entry.editedAt.toInstant() }
                    .thenByDescending { it.entry.id.toString() },
            )
    }

    /** "Typed in", "Health Connect". */
    fun sourceText(source: GoalEntrySource): String = when (source) {
        GoalEntrySource.Hand -> "Typed in"
        GoalEntrySource.HealthConnect -> "Health Connect"
        GoalEntrySource.ScreenTime -> "Screen time"
        GoalEntrySource.Beeminder -> "Beeminder"
        GoalEntrySource.Calendar -> "Calendar"
    }
}
