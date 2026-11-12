package com.accountabilibuddy.companion.ui.goals

import java.util.UUID

/**
 * What the Goals list hands on. R6a draws the list; R6b1 builds a goal's page and the goal menu (the screen owns the menu). The sheets
 * still to come (New goal, Edit area, Options › Areas, Settings › Goals) are wired to a short note until then.
 *
 * @property onOpenStats The header's Stats pill: the Stats screen, unchanged (plan R6.1).
 * @property onNewGoal The + chip: New goal (step 1).
 * @property onOpenGoal A tap on a row: the goal's page.
 * @property onMessage A short note for the user (a change refused, a screen that comes later).
 * @property onEditArea A hold on an area's title, or its ✎ (with a mouse or keyboard): Edit area.
 * @property onOpenAreas Options › Areas: rename, pick icons, reorder, add.
 * @property onOpenGoalsSettings Options › Goals settings: weekly review, reminders, Beeminder, pausing.
 */
data class GoalsActions(
    val onOpenStats: () -> Unit,
    val onNewGoal: () -> Unit,
    val onOpenGoal: (UUID) -> Unit,
    val onMessage: (String) -> Unit,
    val onEditArea: (UUID) -> Unit,
    val onOpenAreas: () -> Unit,
    val onOpenGoalsSettings: () -> Unit,
)
