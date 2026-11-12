package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * The phone's Edit area and Options › Areas rules, the PC's `GoalAreaEditorViewModel` / `GoalAreaOrder` rule for rule: the name every
 * area needs, the icons offered, the order (positions 1..N, saving only the areas that moved), and when an area may be deleted
 * (only an empty one here: its goals move first, from each goal's menu).
 */
object GoalAreaRules {
    /** The icon names an area can take (the PC's list; each has an icon on both platforms). */
    val ICONS: List<String> = listOf(
        "work", "school", "fitness_center", "home", "self_improvement", "menu_book", "translate", "code", "music_note", "palette", "savings",
        "water_drop", "bedtime", "directions_run", "track_changes",
    )

    /** Why [name] can't be an area's name, or null. */
    fun nameProblem(name: String): String? {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return "Give the area a name."
        return if (trimmed.length > GoalDrafts.MAX_NAME_LENGTH) "Keep the name under ${GoalDrafts.MAX_NAME_LENGTH} characters." else null
    }

    /** Where a new area goes: after the last live one. */
    fun nextPosition(areas: List<GoalArea>): Double = (areas.filter { it.isLive }.maxOfOrNull { it.position } ?: 0.0) + 1.0

    /**
     * The live areas that change place when [areaId] moves to [index] among them (clamped), with their new positions 1..N; only the
     * areas whose position changed are returned, in order. Empty when [areaId] isn't a live area.
     */
    fun moved(areas: List<GoalArea>, areaId: UUID, index: Int): List<GoalArea> {
        val ordered = areas.filter { it.isLive }.sortedBy { it.position }.toMutableList()
        val moving = ordered.firstOrNull { it.id == areaId } ?: return emptyList()
        ordered.remove(moving)
        ordered.add(index.coerceIn(0, ordered.size), moving)
        return ordered.mapIndexedNotNull { i, area ->
            val position = (i + 1).toDouble()
            if (area.position != position) area.copy(position = position) else null
        }
    }

    /** Whether the area holds a live goal (it then can't be deleted here). */
    fun goalCount(goals: List<Goal>, areaId: UUID): Int = goals.count { it.isLive && it.areaId == areaId }

    /** Why [areaId] can't be deleted now, or null when it holds no live goal. */
    fun deleteProblem(goals: List<Goal>, areaId: UUID): String? {
        val count = goalCount(goals, areaId)
        if (count == 0) return null
        return if (count == 1) "Move its goal to another area first (Move to area in the goal's menu)." else "Move its $count goals to another area first (Move to area in each goal's menu)."
    }
}
