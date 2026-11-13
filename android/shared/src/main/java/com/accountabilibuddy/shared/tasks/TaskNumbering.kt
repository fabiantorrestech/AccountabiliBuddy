package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * Task numbers (ADR 76; the PC's TaskNumbering): permanent and sequential per account once final. Only the numbering device hands
 * out final numbers; every other device gives a temporary one until the final one arrives.
 */
object TaskNumbering {
    const val PC_KIND = "pc"
    const val PHONE_KIND = "phone"

    /**
     * The device that gives final numbers: the PC with the lowest id (ids compared as lower-case text, Q26) among the group's devices;
     * a group without a PC numbers on its phone with the lowest id (Q3; a PC never sees such a group, so it has no rule for it).
     * Null when there is neither.
     */
    fun numberingDevice(devices: List<Pair<String, String>>): String? {
        val known = devices.map { (id, kind) -> id.lowercase() to kind }.filter { it.first != FieldStamp.NOBODY }
        return known.filter { it.second.equals(PC_KIND, ignoreCase = true) }.minOfOrNull { it.first }
            ?: known.filter { it.second.equals(PHONE_KIND, ignoreCase = true) }.minOfOrNull { it.first }
    }

    /** The next unused number: one above every number and former number among [tasks]. */
    fun nextFree(tasks: List<TaskItem>): Int {
        var highest = 0
        for (task in tasks) {
            highest = maxOf(highest, task.number?.value ?: 0)
            highest = maxOf(highest, task.formerNumbers.orEmpty().maxOrNull() ?: 0)
        }

        return highest + 1
    }

    fun newNumber(known: List<TaskItem>, isNumberingDevice: Boolean): TaskNumber = TaskNumber(nextFree(known), temporary = !isNumberingDevice)

    /**
     * On the numbering device ([self] = [numberingDevice]): every live task gets a final number, in creation order. A final number
     * stays with its first holder; a temporary one becomes final as it is when it is free, else the task gets the next free number
     * and keeps the old one in [TaskItem.formerNumbers]. Elsewhere: the list unchanged. [alsoUsed]: tasks whose numbers are taken too
     * (History's snapshots).
     */
    fun assignFinal(
        tasks: List<SyncTaskItem>,
        self: String,
        numberingDevice: String?,
        now: OffsetDateTime,
        alsoUsed: List<TaskItem> = emptyList(),
    ): List<SyncTaskItem> {
        val result = tasks.toMutableList()
        if (numberingDevice == null || !numberingDevice.equals(self, ignoreCase = true)) {
            return result
        }

        val finals = alsoUsed.mapNotNull { it.number }.filter { !it.temporary }.map { it.value }.toHashSet()
        var next = nextFree(result.map { it.task } + alsoUsed)
        val order = result.indices
            .filter { result[it].task.isLive }
            .sortedWith(compareBy<Int> { DotNetTime.ticks(result[it].task.createdAt) }.thenBy { result[it].id })

        // Finals first, in order: the first holder of a number keeps it; a repeat is renumbered below.
        val needNumber = ArrayList<Int>()
        for (index in order) {
            val number = result[index].task.number
            if (number != null && !number.temporary && finals.add(number.value)) {
                continue
            }

            needNumber += index
        }

        for (index in needNumber) {
            val item = result[index]
            val old = item.task.number
            val usedAsFormer = old != null &&
                (result.map { it.task } + alsoUsed).any { it.id != item.task.id && it.formerNumbers?.contains(old.value) == true }
            val value: Int
            if (old != null && old.temporary && old.value !in finals && !usedAsFormer) {
                value = old.value
            } else {
                value = next
                next++
            }

            finals += value
            var former = item.task.formerNumbers
            if (old != null && old.value != value) {
                former = (former.orEmpty() + old.value).distinct().sorted()
            }

            val changed = item.task.copy(number = TaskNumber(value), formerNumbers = former)
            val stamped = TaskEdits.edit(item, TaskField.NUMBER, changed, now, self)
            result[index] = stamped.copy(task = stamped.task.copy(formerNumbers = former))
        }

        return result
    }
}
