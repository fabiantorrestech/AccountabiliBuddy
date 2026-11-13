package com.accountabilibuddy.shared.goals

/**
 * The goal menu's rows for one goal, in the PC goal menu's order (brief §4.3: "the same list as the PC context menu; keep the order
 * identical"): the name; Start a session │ the kind's quick log (Tick today / Log a value… / Add time by hand… / Open its tasks, then
 * Open in Beeminder when linked) │ Edit goal…, Reminders, Move to area, Show on Today, Pause or Resume │ Delete goal… (red), or for
 * Deep work Track time only. [GoalMenuItemId.Separator] rows are the thin dividers between the groups.
 *
 * On the phone: Deep work's daily target is the PC's (ADR 29; with no PC in the group, [noPc], it is the phone's own and is set in Edit goal), so its last row is greyed; Beeminder is read on the PC, so Open in
 * Beeminder is greyed; "Open its tasks" is "Coming soon" (plan §3b 2). Whether a row's screen exists yet is the screen's business.
 */
object GoalMenuPlan {
    /**
     * [todayState]: the goal's state today (a habit's tick, "8 of 15 min so far"). [startValue]: what Start a session… starts by
     * default ("Default"), shown at its right; [startBlocked]: why a session can't be started from this phone now (no PC linked, a
     * session running, an answer awaited), which greys the row and is its note. [reminderNext]: "Next: today 8:00 PM", the Reminders row's note.
     */
    fun of(
        goal: Goal,
        todayState: GoalDayState,
        paused: Boolean,
        reminderCount: Int,
        areaName: String?,
        startValue: String? = null,
        startBlocked: String? = null,
        reminderNext: String? = null,
        noPc: Boolean = false,
    ): List<GoalMenuItem> {
        val items = mutableListOf(GoalMenuItem(GoalMenuItemId.Heading, goal.name))
        items += GoalMenuItem(
            GoalMenuItemId.StartSession, "Start a session…", note = startBlocked ?: "Counts toward ${goal.name}",
            value = startValue.takeIf { startBlocked == null }, enabled = startBlocked == null, opensMore = true,
        )
        items += separator
        items += quickLog(goal, todayState, paused, noPc)
        items += separator
        items += GoalMenuItem(GoalMenuItemId.EditGoal, "Edit goal…", note = "Target, tracking & sources, notes", opensMore = true)
        items += GoalMenuItem(GoalMenuItemId.Reminders, "Reminders", note = reminderNext, value = if (reminderCount == 0) "None" else "$reminderCount set", opensMore = true)
        items += GoalMenuItem(GoalMenuItemId.MoveToArea, "Move to area", value = areaName, opensMore = true)
        items += GoalMenuItem(GoalMenuItemId.ShowOnToday, "Show on Today", note = "On Today's goals card", checked = goal.showOnToday)
        items += when {
            goal.isDeepWork -> GoalMenuItem(GoalMenuItemId.Pause, "Pause goal", note = "Deep work can't be paused", enabled = false)
            paused -> GoalMenuItem(GoalMenuItemId.Pause, "Resume goal", note = "Counts again from today")
            else -> GoalMenuItem(GoalMenuItemId.Pause, "Pause goal", note = "Stops counting and reminders · keeps history")
        }
        items += separator
        items += if (goal.isDeepWork) {
            GoalMenuItem(
                GoalMenuItemId.TrackTimeOnly, "Track time only, no target",
                note = if (noPc) "Set its daily target in Edit goal" else "Its target is set on your PC", enabled = false,
            )
        } else {
            GoalMenuItem(GoalMenuItemId.Delete, "Delete goal…", danger = true)
        }
        return items
    }

    private val separator = GoalMenuItem(GoalMenuItemId.Separator, "")

    private fun quickLog(goal: Goal, state: GoalDayState, paused: Boolean, noPc: Boolean): List<GoalMenuItem> {
        val log = when {
            goal.isDeepWork -> GoalMenuItem(
                GoalMenuItemId.AddTimeByHand, "Add time by hand…",
                note = if (noPc) "Deep work counts focus sessions only" else "Deep work counts focus only: add a session in History on your PC", enabled = false,
            )
            paused -> GoalMenuItem(GoalMenuItemId.TickToday, if (goal.kind == GoalKind.Habit) "Tick today" else "Log today", note = "Paused: nothing to log", enabled = false)
            else -> when (goal.kind) {
                GoalKind.Habit -> {
                    val threshold = GoalTexts.threshold(goal)
                    GoalMenuItem(
                        GoalMenuItemId.TickToday, if (state.hit) "Untick today" else "Tick today",
                        note = threshold?.let { "${GoalTexts.minutes(state.minutes)} of ${GoalTexts.minutes(it)} so far" },
                    )
                }
                GoalKind.Amount -> GoalMenuItem(GoalMenuItemId.LogValue, "Log a value…", opensMore = true)
                GoalKind.Time -> GoalMenuItem(GoalMenuItemId.AddTimeByHand, "Add time by hand…", note = "Counts for the goal, never for a guard", opensMore = true)
                GoalKind.Milestone -> GoalMenuItem(GoalMenuItemId.OpenTasks, "Open its tasks", note = "Coming soon", enabled = false)
            }
        }
        val beeminder = goal.sources.firstOrNull { it.type == GoalSourceType.Beeminder && it.beeminderSlug != null }
        return if (beeminder == null) {
            listOf(log)
        } else {
            listOf(log, GoalMenuItem(GoalMenuItemId.OpenInBeeminder, "Open in Beeminder", note = "Beeminder is read on your PC", value = beeminder.beeminderSlug, enabled = false))
        }
    }
}
