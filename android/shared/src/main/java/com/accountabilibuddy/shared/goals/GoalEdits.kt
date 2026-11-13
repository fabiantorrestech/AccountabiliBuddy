package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import java.time.OffsetDateTime

/**
 * Local changes to a goal, stamped part by part (the PC's `GoalEdits`): only the parts whose value really changed get the new stamp
 * ([now], one tick past the goal's newest time when that is later, by [by], the device id as lower-case text).
 */
object GoalEdits {
    /** A new goal: every part stamped at its edit time by [by]. */
    fun new(goal: Goal, by: String): Goal = goal.copy(stamps = GoalFieldStamps.allAt(FieldStamp(goal.editedAt, by)))

    /** [goal] with only [field] taken from [changed], stamped; [goal] itself when that part didn't change. */
    fun edit(goal: Goal, field: GoalField, changed: Goal, now: OffsetDateTime, by: String): Goal {
        if (GoalFieldValues.canonical(goal, field) == GoalFieldValues.canonical(changed, field)) return goal
        return stamped(goal, listOf(field), GoalFieldValues.with(goal, changed, field), now, by)
    }

    /** [after] with every part that differs from [before] stamped (a store's change); nothing changed: [before]'s stamps and edit time. */
    fun restamp(before: Goal, after: Goal, now: OffsetDateTime, by: String): Goal {
        val old = GoalFieldValues.all(before)
        val now2 = GoalFieldValues.all(after)
        val changed = GoalField.values().filter { old.getValue(it) != now2.getValue(it) }
        if (changed.isEmpty()) {
            return after.copy(stamps = before.stamps ?: GoalFieldStamps.forGoal(before), editedAt = before.editedAt)
        }
        return stamped(before, changed, after, now, by)
    }

    private fun stamped(before: Goal, fields: List<GoalField>, changed: Goal, now: OffsetDateTime, by: String): Goal {
        var stamps = GoalFieldStamps.forGoal(before)
        val held = before.deletedAt?.let { if (DotNetTime.compare(it, before.editedAt) > 0) it else before.editedAt } ?: before.editedAt
        // The next-stamp rule for the goal, then one tick past the newest part stamp when that is not earlier.
        val at = FieldStamps.next(now, held, stamps.newest)
        val stamp = FieldStamp(at, by)
        for (field in fields) stamps = stamps.with(field, stamp)
        return changed.copy(editedAt = if (DotNetTime.compare(at, changed.editedAt) > 0) at else changed.editedAt, stamps = stamps)
    }
}
