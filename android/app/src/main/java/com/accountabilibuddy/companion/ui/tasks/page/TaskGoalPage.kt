package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.goals.GoalIcons
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalTaskLinks

/** The goals a task can be linked to (GS5b; the PC's `LinkableGoals`): the live goals in their order, never Deep work (it counts all focus). */
fun linkableGoals(lists: GoalLists): List<Goal> = GoalTaskLinks.linkable(lists)

/** The live goal named by a task's goal id, or null for none (a link to a goal that no longer exists shows as none; the id stays on the task). */
fun linkedGoalOf(goals: List<Goal>, goalId: String?): Goal? = GoalTaskLinks.linkedGoal(goals, goalId)

/** The goals the "Link to goal" page lists, as the sheet sees them now. */
@Composable
fun rememberLinkableGoals(): List<Goal> {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    return remember(lists) { linkableGoals(lists) }
}

/**
 * The rows of the "Link to goal" page of the task sheet and of select mode's Options (GS5b; the PC's `TaskGoalMenu`): each linkable goal
 * (its icon, the task's own with a check; one for several tasks ticks none), then **Unlink** last (also with no goal left, while a task still carries
 * a link to a deleted one: [hasLink]). A pick links the tasks ([ShelfController.setGoal],
 * one stamped edit each with one Undo) and calls [onDone]. [goals] is [rememberLinkableGoals].
 */
@Composable
fun TaskGoalRows(goals: List<Goal>, current: String?, hasLink: Boolean, ids: List<String>, controller: ShelfController, onDone: () -> Unit) {
    if (goals.isEmpty() && !hasLink) {
        SheetFoot("No goals yet. Make one on the Goals page, then link tasks to it here.")
        return
    }

    goals.forEach { goal ->
        AbSheetRow(
            goal.name, onClick = { controller.setGoal(ids, goal.id.toString(), goal.name); onDone() }, icon = GoalIcons.of(goal.icon), role = Role.RadioButton,
            trailing = { AbSheetCheck(current != null && goal.id.toString().equals(current, ignoreCase = true)) },
        )
    }
    if (goals.isNotEmpty()) Box(Modifier.fillMaxWidth().padding(vertical = 4.dp).height(1.dp).background(Ab.colors.line))
    // Unlink is always there while a task carries a link, also one to a goal that was deleted (the id stays on the task).
    AbSheetRow("Unlink", onClick = { controller.setGoal(ids, null, null); onDone() }, icon = Icons.Outlined.LinkOff)
    SheetFoot("Focus time on a linked task counts toward the goal when the goal has the source \"A task carries this goal\"; a finished one counts for a milestone.")
}
