package com.accountabilibuddy.shared.goals

import java.util.UUID

/** The Progress tracking lines, the PC's `GoalDetailViewModel.SourceLines` rule for rule. */
object GoalSourceLines {
    /** [labelName]: a task label's name by id, or null. [hiddenNote]: for a label this phone doesn't show (ADR 89), the note that names the device, or null. */
    fun of(goal: Goal, threshold: Double?, labelName: (UUID) -> String?, hiddenNote: (UUID) -> String? = { null }): List<GoalSourceLine> {
        if (goal.isDeepWork) {
            return listOf(
                GoalSourceLine("All your counted focus, on every device (the guards' rule)"),
                GoalSourceLine("No hand entries: time added by hand never feeds a guard"),
            )
        }

        val lines = mutableListOf<GoalSourceLine>()
        // Hand entries always count (the line after the rules says so), so a Hand rule needs no line of its own.
        for (rule in GoalSources.countedFor(goal).filter { it.type != GoalSourceType.Hand }) {
            val label = rule.labelId?.let(labelName)
            val soon = GoalSources.availabilityOf(rule.type) == GoalSourceAvailability.ComingSoon
            var text = when (rule.type) {
                GoalSourceType.FocusAll -> "All your counted focus"
                GoalSourceType.FocusLabel -> if (label == null) (rule.labelId?.let(hiddenNote) ?: "Focus time on tasks with its label") else "Focus time on tasks labelled $label"
                GoalSourceType.FocusTag -> "Focus time on tasks tagged #${rule.tag?.trimStart('#') ?: ""}"
                GoalSourceType.CalendarCategory -> "Time in a calendar category (this PC)"
                GoalSourceType.HealthSteps -> "Daily steps from your phone"
                GoalSourceType.HealthWeight -> "Weight from Health Connect"
                GoalSourceType.HealthSleep -> "Sleep from Health Connect"
                GoalSourceType.ScreenTime -> "Screen time, measured on your phone"
                GoalSourceType.Beeminder -> "Beeminder · ${rule.beeminderSlug ?: ""}"
                GoalSourceType.GoalField -> "Tasks that carry this goal"
                GoalSourceType.MilestoneTasks -> "Its milestone tasks"
                else -> "By hand"
            }
            if (!rule.automatic) text += " (off)"
            lines += GoalSourceLine(if (soon) "$text · Coming soon" else text, soon)
        }

        if (threshold != null) lines += GoalSourceLine("Ticks itself at ${GoalTexts.minutes(threshold)} in a day")
        lines += GoalSourceLine(if (goal.kind == GoalKind.Habit) "Ticking it by hand here or on Today" else "Values and time you add by hand")
        lines += GoalSourceLine("Tasks that carry this goal · Coming soon", true)
        return lines
    }
}
