package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime
import kotlinx.serialization.json.JsonObject

/**
 * When each part of a goal was last changed and by which device (the PC's `GoalFieldStamps`), as the shared [FieldStamp] without
 * bases. A part without a stamp is simply absent from [parts]; [forGoal] fills it. [extra]: stamp names of a newer app, kept.
 */
data class GoalFieldStamps(
    val parts: Map<GoalField, FieldStamp> = emptyMap(),
    val extra: JsonObject = JsonObject(emptyMap()),
) {
    fun of(field: GoalField): FieldStamp? = parts[field]

    fun with(field: GoalField, stamp: FieldStamp?): GoalFieldStamps =
        copy(parts = if (stamp == null) parts - field else parts + (field to stamp))

    /** The newest stamp time of any part (the first of equal ones); null with none. */
    val newest: OffsetDateTime?
        get() {
            var best: OffsetDateTime? = null
            for (field in GoalField.values()) {
                val at = parts[field]?.at ?: continue
                if (best == null || DotNetTime.compare(at, best) > 0) best = at
            }
            return best
        }

    companion object {
        val NONE = GoalFieldStamps()

        fun allAt(stamp: FieldStamp): GoalFieldStamps = GoalFieldStamps(GoalField.values().associateWith { stamp })

        /** Every part's stamp: the goal's own, and for a part without one the goal's edit time by nobody. */
        fun forGoal(goal: Goal): GoalFieldStamps {
            val unstamped = FieldStamp(goal.editedAt, FieldStamp.NOBODY)
            val own = goal.stamps ?: return allAt(unstamped)
            var all = own
            for (field in GoalField.values()) {
                if (own.of(field) == null) all = all.with(field, unstamped)
            }
            return all
        }
    }
}
