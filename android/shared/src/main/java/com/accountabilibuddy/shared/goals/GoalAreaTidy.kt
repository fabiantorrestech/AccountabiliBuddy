package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID

/**
 * Where a live goal goes when its area isn't live (a deleted area, a backup's goal whose area didn't come): to "Work", made again from
 * the given template (its fixed id, name, icon and colour) when it is missing or a tombstone; the PC's `GoalAreaTidy`, rule for rule
 * (parity fixtures). Stamps depend only on the lists, never on the clock, so two devices tidying the same lists get the same result
 * (ADR 79): a moved goal is stamped one tick after the later of its own edit and its old area's deletion; a "Work" made again in place
 * of its tombstone keeps that place and is stamped one tick after it, and never before a goal moved into it; a missing one goes last
 * (after every area, tombstones included) stamped as the latest goal moved into it.
 */
object GoalAreaTidy {
    /** The device id a tidy move is stamped with (never the empty id, which reads as the untouched Deep work seed); the PC's `GoalAreaTidy.StampId`. */
    val STAMP_ID: String = "d33b0a11-6f0c-4e2a-9d51-0000000000b1"

    /** The areas and goals with every live goal in a live area. */
    fun rehome(areas: List<GoalArea>, goals: List<Goal>, work: GoalArea): Pair<List<GoalArea>, List<Goal>> {
        val liveAreas = areas.filter { it.isLive }.map { it.id }.toSet()
        if (goals.none { it.isLive && it.areaId !in liveAreas }) return areas to goals

        val byId = HashMap<UUID, GoalArea>()
        areas.forEach { byId.putIfAbsent(it.id, it) }
        var workStamp: OffsetDateTime? = null
        val placed = goals.map { goal ->
            if (!goal.isLive || goal.areaId in liveAreas) return@map goal
            val gone = byId[goal.areaId]
            val basis = if (gone == null) goal.editedAt else later(goal.editedAt, stampOf(gone))
            val stamp = oneTickAfter(basis)
            workStamp = workStamp?.let { later(it, stamp) } ?: stamp
            goal.copy(areaId = work.id, editedAt = stamp)
        }

        val tidied = areas.toMutableList()
        val index = tidied.indexOfFirst { it.id == work.id }
        if (index < 0) {
            val position = if (tidied.isEmpty()) 0.0 else tidied.maxOf { it.position } + 1
            tidied += work.copy(position = position, editedAt = workStamp!!, deletedAt = null)
        } else if (!tidied[index].isLive) {
            val tombstone = tidied[index]
            tidied[index] = work.copy(position = tombstone.position, editedAt = later(oneTickAfter(stampOf(tombstone)), workStamp!!), deletedAt = null)
        }

        return tidied to placed
    }

    // An area's newest stamp: its deletion when later than its edit.
    private fun stampOf(area: GoalArea): OffsetDateTime = area.deletedAt?.let { later(area.editedAt, it) } ?: area.editedAt

    // The later of two instants; the first on a tie (so both platforms keep the same offset).
    private fun later(first: OffsetDateTime, second: OffsetDateTime): OffsetDateTime = if (second.toInstant().isAfter(first.toInstant())) second else first

    // .NET's AddTicks(1): 100 ns later, the same offset.
    private fun oneTickAfter(time: OffsetDateTime): OffsetDateTime = time.plusNanos(GoalListRules.TICK_NANOS)
}
