package com.accountabilibuddy.shared.goals

/** One row of a goal page's Tasks card: a task's title (private text, drawn on the page only) and whether it is finished. */
data class GoalTaskRow(val title: String, val done: Boolean)

/**
 * The goal page's **Tasks** card (GS5, the PC's `GoalDetailViewModel.ShowTasks`): the tasks that carry the goal, the open ones first, then
 * the finished ones latest first, at most [MAX_ROWS] rows, and a foot ("2 of 5 finished", "3 more not shown", or what to do when none is
 * linked). It shows when the goal has a task source or a task linked to it, never for Deep work; with links but no task source it says they count once the source is added ([note]).
 */
data class GoalTasksCard(val rows: List<GoalTaskRow>, val linked: Int, val foot: String, val note: String? = null) {
    companion object {
        /** How many tasks the card lists before "N more". */
        const val MAX_ROWS = 8

        const val NONE_LINKED = "No task is linked yet. Open a task's menu › Link to goal"

        /** The card for [goal] from [links] (any goals' links; this goal's are read), or null when the page shows none. */
        fun of(goal: Goal, links: List<GoalTaskLink>): GoalTasksCard? {
            val mine = links.filter { it.goalId == goal.id }
                .sortedWith(
                    compareBy<GoalTaskLink> { it.isDone }
                        .thenByDescending { it.doneAt?.toInstant() ?: java.time.Instant.MIN }
                        .thenBy(String.CASE_INSENSITIVE_ORDER) { it.title },
                )
            val hasSource = goal.sources.any { it.type == GoalSourceType.GoalField || it.type == GoalSourceType.MilestoneTasks }
            if (goal.isDeepWork || (mine.isEmpty() && !hasSource)) return null
            val done = mine.count { it.isDone }
            val foot = when {
                mine.isEmpty() -> NONE_LINKED
                mine.size > MAX_ROWS -> "$done of ${mine.size} finished · ${mine.size - MAX_ROWS} more not shown"
                else -> "$done of ${mine.size} finished"
            }
            // Q1132: tasks are linked but the goal has no task source, so nothing counts yet.
            val source = if (goal.kind == GoalKind.Milestone) "Milestone tasks" else "A task carries this goal"
            val note = if (mine.isNotEmpty() && !hasSource) "They don't count toward this goal until you add the source \"$source\"." else null
            return GoalTasksCard(mine.take(MAX_ROWS).map { GoalTaskRow(it.title, it.isDone) }, mine.size, foot, note)
        }
    }
}
