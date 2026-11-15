package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.shared.goals.GoalDraft
import com.accountabilibuddy.shared.goals.GoalReminder

/**
 * Whether the goal editor holds changes that aren't saved (FX-A): the draft, its reminders or the two reminder rules differ from what the
 * sheet opened with. A new goal that was opened and left alone is not dirty; closing it asks nothing. Pure (checked on its own).
 */
object GoalEditorDirty {
    fun isDirty(
        draft: GoalDraft, start: GoalDraft,
        reminders: List<GoalReminder>, startReminders: List<GoalReminder>,
        rules: Pair<Boolean, Boolean>, startRules: Pair<Boolean, Boolean>,
    ): Boolean = draft != start || reminders != startReminders || rules != startRules

    /** The dialog's question: "Discard this goal?" for a goal that doesn't exist yet, else "Discard your changes?". */
    fun question(newGoal: Boolean): String = if (newGoal) "Discard this goal?" else "Discard your changes?"
}
