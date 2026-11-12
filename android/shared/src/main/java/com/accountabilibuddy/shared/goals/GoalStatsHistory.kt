package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * Stats for one goal in words (round 2 R6b4), the PC's `StatsGoalHistoryViewModel` text for text: the line under its name, its age,
 * its streak and its paused stretches ("Paused Sep 14 – Oct 30"). The goal's name is private text: on screen only, never in a log.
 *
 * @property subtitle "Learning · daily habit · 15 min a day · 6 months going".
 * @property ageText "Since Mon 5/4/2026 · 191 days (27 weeks) · paused 46 days".
 * @property streakText "Streak: 12", or "No streak right now".
 * @property pausedStretches "Paused Sep 14 – Oct 30", "Paused since Mon 11/2", oldest first; empty when never paused.
 */
data class GoalStatsHistory(val subtitle: String, val ageText: String, val streakText: String, val pausedStretches: List<String>) {
    companion object {
        /** What the hatching means, under the paused stretches. */
        const val PAUSED_NOTE = "Paused days are hatched: neither hit nor missed, so streaks and pace aren't punished."

        fun of(facts: GoalFacts, areaName: String?): GoalStatsHistory {
            val goal = facts.goal
            val today = facts.today
            val area = if (areaName.isNullOrEmpty()) "" else "$areaName · "
            val streak = when (facts.streak) {
                0 -> "No streak right now"
                else -> "Streak: ${facts.streak}"
            }
            return GoalStatsHistory(
                "$area${GoalTexts.kindText(goal)} · ${GoalTexts.targetLine(goal, today)} · ${facts.age.short} going",
                facts.age.tooltip,
                streak,
                pausedTexts(goal, today),
            )
        }

        /** "Paused Sep 14 – Oct 30" for each stretch (the day before it was turned back on is its last), oldest first. */
        fun pausedTexts(goal: Goal, today: LocalDate): List<String> =
            goal.pauses.orEmpty().sortedBy { it.from }.map { stretch ->
                val resumed = stretch.resumedOn
                if (resumed == null) {
                    "Paused since ${GoalTexts.date(stretch.from, today)}"
                } else {
                    val last = resumed.minusDays(1)
                    if (last < stretch.from) {
                        "Paused ${GoalTrends.shortDate(stretch.from, today)}"
                    } else {
                        "Paused ${GoalTrends.shortDate(stretch.from, today)} – ${GoalTrends.shortDate(last, today)}"
                    }
                }
            }
    }
}
