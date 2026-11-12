package com.accountabilibuddy.shared.goals

/**
 * Per stored goal list, the items kept as written because they hold a value this version doesn't know (ADR 79 forward compatibility,
 * [GoalNewerItem]): never counted or shown, never dropped, written back at their places on every save; the PC's `GoalNewerLists`.
 */
data class GoalNewerLists(
    val areas: List<GoalNewerItem> = emptyList(),
    val goals: List<GoalNewerItem> = emptyList(),
    val entries: List<GoalNewerItem> = emptyList(),
    val reminders: List<GoalNewerItem> = emptyList(),
    val reviews: List<GoalNewerItem> = emptyList(),
) {
    companion object {
        val NONE = GoalNewerLists()
    }
}
