package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID
import kotlin.math.floor

/** Builds a [GoalDayPanel] from a day's state: the PC's `GoalDayPanelViewModel.Show` rule for rule. */
object GoalDayPanels {
    /** [labelName]: a task label's name by id (the source line), or null. */
    fun of(goal: Goal, state: GoalDayState, today: LocalDate, labelName: (UUID) -> String?): GoalDayPanel {
        val past = state.day != today
        val threshold = GoalTexts.threshold(goal)
        val unit = goal.target.unit
        val (big, of) = when {
            goal.kind == GoalKind.Habit && threshold != null -> floor(state.minutes).toInt().toString() to "of ${GoalTexts.minutes(threshold)}"
            goal.kind == GoalKind.Habit -> (if (state.hit) "Done" else "Not yet") to null
            goal.kind == GoalKind.Time -> {
                val amount = goal.target.amount
                GoalTexts.minutes(state.value) to (if (goal.target.period == GoalPeriod.Day && amount != null) "of ${GoalTexts.minutes(amount)}" else null)
            }
            else -> {
                val daily = goal.target.amount
                (if (state.hasValue) GoalTexts.value(state.value, unit) else "–") to
                    (if (goal.target.period == GoalPeriod.Day && daily != null) "of ${GoalTexts.shortValue(daily, unit)}" else null)
            }
        }
        val (status, tone) = statusOf(goal, state)
        val deepWork = goal.isDeepWork
        val open = state.status != GoalDayStatus.Paused && state.status != GoalDayStatus.NotDue
        val note = when {
            deepWork -> "Deep work counts focus only: no hand entries."
            state.status == GoalDayStatus.Paused -> "Paused days are neither hit nor missed."
            !past && threshold != null -> "It also ticks itself at ${GoalTexts.minutes(threshold)}."
            state.hit -> "Changes count toward that week and your streaks."
            past -> "Forgot to log it? Ticking counts the day and can rejoin your streak."
            else -> null
        }
        return GoalDayPanel(
            day = state.day,
            isPast = past,
            title = if (past) GoalTexts.date(state.day, today) else "Today · ${GoalTexts.date(today, today)}",
            big = big,
            of = of,
            status = status,
            tone = tone,
            source = sourceLine(goal, state, today, labelName),
            note = note,
            canTick = !deepWork && goal.kind == GoalKind.Habit && open,
            tickText = if (state.hit) "Untick" else if (past) "Tick it" else "Tick it now",
            canEdit = !deepWork && goal.kind != GoalKind.Habit && open,
            editCaption = if (goal.kind == GoalKind.Time) "Minutes to add by hand for ${GoalTexts.date(state.day, today)}" else "Value for ${GoalTexts.date(state.day, today)}",
        )
    }

    private fun statusOf(goal: Goal, state: GoalDayState): Pair<String, GoalDayPanel.Tone> {
        fun short(gap: Double) = if (goal.kind == GoalKind.Habit || goal.target.unit == GoalUnit.Minutes) GoalTexts.minutes(gap) else GoalTexts.value(gap, goal.target.unit)
        return when (state.status) {
            GoalDayStatus.CountedTickedItself -> (if (state.hit) "Counted · ticked itself" else "Counted") to GoalDayPanel.Tone.Teal
            GoalDayStatus.Counted -> (if (state.byHand) "Counted · by hand" else "Counted") to GoalDayPanel.Tone.Teal
            GoalDayStatus.NotCounted -> "Not counted · ${short(state.short)} short" to GoalDayPanel.Tone.Amber
            GoalDayStatus.Missed -> "Missed" to GoalDayPanel.Tone.Amber
            GoalDayStatus.InProgress -> (if (state.short > 0) "In progress · ${short(state.short)} to go" else "In progress") to GoalDayPanel.Tone.Grey
            GoalDayStatus.Paused -> "Paused" to GoalDayPanel.Tone.Grey
            GoalDayStatus.NotDue -> "Not due" to GoalDayPanel.Tone.Grey
        }
    }

    private fun sourceLine(goal: Goal, state: GoalDayState, today: LocalDate, labelName: (UUID) -> String?): String {
        val soFar = if (state.day == today) " so far" else ""
        val minutes = GoalTexts.minutes(state.minutes)
        val parts = mutableListOf<String>()
        for (source in state.sources) {
            when (source) {
                GoalSourceType.FocusAll -> parts += "$minutes of counted focus$soFar"
                GoalSourceType.FocusLabel -> {
                    val label = goal.sources.filter { it.type == GoalSourceType.FocusLabel && it.labelId != null }.mapNotNull { labelName(it.labelId!!) }.firstOrNull()
                    parts += if (label == null) "$minutes of focus on its label$soFar" else "$minutes of focus on the $label label$soFar"
                }
                GoalSourceType.FocusTag -> {
                    val tag = goal.sources.firstOrNull { it.type == GoalSourceType.FocusTag }?.tag
                    parts += if (tag == null) "$minutes of focus on its tag$soFar" else "$minutes of focus tagged #${tag.trimStart('#')}$soFar"
                }
                GoalSourceType.Hand -> parts += if (goal.kind == GoalKind.Habit) (if (state.hit) "ticked by hand" else "unticked by hand") else "added by hand"
                GoalSourceType.CalendarCategory -> parts += "time from your calendar"
                GoalSourceType.HealthSteps, GoalSourceType.HealthWeight, GoalSourceType.HealthSleep -> parts += "from Health Connect"
                GoalSourceType.ScreenTime -> parts += "from screen time on your phone"
                GoalSourceType.Beeminder -> parts += "from Beeminder"
                else -> Unit
            }
        }

        if (parts.isNotEmpty()) {
            val joined = parts.joinToString(" · ")
            return joined[0].uppercaseChar() + joined.substring(1)
        }

        return if (goal.isDeepWork) "No counted focus${if (state.day == today) " yet" else ""}" else "Nothing logged for this day"
    }
}
