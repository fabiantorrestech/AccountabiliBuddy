package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * The order of the goals inside one area (board Goals-Phone-Area: "Press and hold a goal to drag it into a new order"), by the same
 * rule as the areas' own order ([GoalAreaRules.moved]): positions 1..N, and only the goals whose place changed are saved. Also where
 * an area's goals go when it is deleted (brief §4.3: "Delete asks first and says where its goals go").
 */
object GoalOrderRules {
    /** The live goals of [areaId], lowest position first (ties by id, so the order never flickers). */
    fun inArea(goals: List<Goal>, areaId: UUID): List<Goal> =
        goals.filter { it.isLive && it.areaId == areaId }.sortedWith(compareBy({ it.position }, { it.id.toString() }))

    /**
     * The goals of [areaId] that change place when they are put in [order] (ids; every live goal of the area exactly once), with
     * their new positions 1..N. Empty when [order] doesn't name exactly the area's goals, or nothing moved.
     */
    fun reordered(goals: List<Goal>, areaId: UUID, order: List<UUID>): List<Goal> {
        val mine = inArea(goals, areaId)
        if (order.size != mine.size || order.toSet() != mine.map { it.id }.toSet()) return emptyList()
        val byId = mine.associateBy { it.id }
        return order.mapIndexedNotNull { index, id ->
            val goal = byId.getValue(id)
            val position = (index + 1).toDouble()
            if (goal.position != position) goal.copy(position = position) else null
        }
    }

    /** The area a deleted area's goals move to: the first other live area in the areas' order; null when there is none. */
    fun heirOf(areas: List<GoalArea>, areaId: UUID): GoalArea? =
        areas.filter { it.isLive && it.id != areaId }.minWithOrNull(compareBy({ it.position }, { it.id.toString() }))

    /** The goals of [from] as they are once moved to [to]: after its last goal, keeping their order. Empty when [from] holds none. */
    fun movedTo(goals: List<Goal>, from: UUID, to: UUID): List<Goal> {
        val last = goals.filter { it.isLive && it.areaId == to }.maxOfOrNull { it.position } ?: 0.0
        return inArea(goals, from).mapIndexed { index, goal -> goal.copy(areaId = to, position = last + index + 1) }
    }
}
