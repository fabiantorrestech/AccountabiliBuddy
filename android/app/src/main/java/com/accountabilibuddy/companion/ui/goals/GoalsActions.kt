package com.accountabilibuddy.companion.ui.goals

import java.util.UUID

/**
 * What the Goals list hands on. R6a draws the list; the sheets these open (New goal, Edit area, the goal menu, a goal's page,
 * Options › Areas, Settings › Goals) are R6b's: until then the screen wires each to a short note.
 *
 * @property onOpenStats The header's Stats pill: the Stats screen, unchanged (plan R6.1).
 * @property onNewGoal The + chip: New goal (step 1).
 * @property onOpenGoal A tap on a row: the goal's page.
 * @property onGoalMenu A hold on a row, or its ⋮: the goal menu (the same rows and order as the PC's).
 * @property onEditArea A hold on an area's title, or its ✎ (with a mouse or keyboard): Edit area.
 * @property onOpenAreas Options › Areas: rename, pick icons, reorder, add.
 * @property onOpenGoalsSettings Options › Goals settings: weekly review, reminders, Beeminder, pausing.
 */
data class GoalsActions(
    val onOpenStats: () -> Unit,
    val onNewGoal: () -> Unit,
    val onOpenGoal: (UUID) -> Unit,
    val onGoalMenu: (UUID) -> Unit,
    val onEditArea: (UUID) -> Unit,
    val onOpenAreas: () -> Unit,
    val onOpenGoalsSettings: () -> Unit,
)
