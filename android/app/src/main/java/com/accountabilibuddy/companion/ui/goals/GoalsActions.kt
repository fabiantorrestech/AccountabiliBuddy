package com.accountabilibuddy.companion.ui.goals

import java.util.UUID

/**
 * What the Goals list hands on to the shell. R6a draws the list; R6b1 builds a goal's page and the goal menu; R6b2 adds New goal, Edit
 * goal, Edit area and Options › Areas as sheets the screen owns, so the shell only opens the places it navigates to.
 *
 * @property onOpenStats The header's Stats pill: the Stats screen, unchanged (plan R6.1).
 * @property onOpenGoal A tap on a row: the goal's page.
 * @property onMessage A short note for the user (a change refused, a screen that comes later).
 * @property onOpenGoalsSettings Options › Goals settings: the Settings › Goals page.
 * @property session What the goal menu's "Start a session…" starts with ([GoalSessionStart]); null greys the row.
 */
data class GoalsActions(
    val onOpenStats: () -> Unit,
    val onOpenGoal: (UUID) -> Unit,
    val onMessage: (String) -> Unit,
    val onOpenGoalsSettings: () -> Unit,
    val session: GoalSessionStart? = null,
)
