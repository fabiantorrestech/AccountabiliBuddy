package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Merges two label lists (ADR 76, 89; the PC's TaskLabelMerge): item by item, newest version first ([SharedListMerge]), then each live
 * label's "Rings on" and "Only on" on their own: the copy with the newest stamp ([TaskLabel.ringOnStamp], [TaskLabel.onlyOnStamp]) gives
 * the field (a stamped list beats an unstamped one; between two stamps the later time, then the device id, then the list; a stamp far ahead
 * counts as the future limit ahead). So a newer change to the label's name or colour made without the newest rings on or device list never
 * takes the choice back, and every device picks the same whatever order the lists come in. A stamp with a null "Only on" is a choice of
 * "shared" and wins like any other. Label tombstones are kept for good (ids and stamps only, no text): a tombstone is what stops an older
 * phone's category bringing a deleted label back.
 */
object TaskLabelMerge {
    // One stamped field of a label: how its stamp, its text and its value are read and set.
    private class Field(
        val stamp: (TaskLabel) -> FieldStamp?,
        val text: (TaskLabel) -> String,
        val take: (TaskLabel, TaskLabel) -> TaskLabel,
        val clamp: (TaskLabel, FieldStamp) -> TaskLabel,
    )

    private val RING_ON = Field(
        { it.ringOnStamp },
        { TaskRingDevices.text(it.ringOn) },
        { label, source -> label.copy(ringOn = source.ringOn, ringOnStamp = source.ringOnStamp) },
        { label, stamp -> label.copy(ringOnStamp = stamp) },
    )

    private val ONLY_ON = Field(
        { it.onlyOnStamp },
        { TaskRingDevices.text(TaskScopes.normalized(it.onlyOn)) },
        { label, source -> label.copy(onlyOn = source.onlyOn, onlyOnStamp = source.onlyOnStamp) },
        { label, stamp -> label.copy(onlyOnStamp = stamp) },
    )

    fun merge(held: List<SyncTaskLabel>, incoming: List<SyncTaskLabel>, now: OffsetDateTime): List<SyncTaskLabel> {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        var merged = SharedListMerge.merge(held.map { clampedItem(it, limit) }, incoming.map { clampedItem(it, limit) }, now.toInstant(), keepTombstones = true)
        val all = (held + incoming).map { it.label }.filter { it.isLive }

        merged = withNewest(merged, all, limit, RING_ON)
        return withNewest(merged, all, limit, ONLY_ON).map(::withStoredOnlyOn)
    }

    // Each live item gets the field of the live copy with the newest stamp; items with no stamped copy stay as the whole-record merge left them.
    private fun withNewest(merged: List<SyncTaskLabel>, all: List<TaskLabel>, limit: OffsetDateTime, field: Field): List<SyncTaskLabel> {
        val newest = HashMap<String, TaskLabel>()
        for (label in all.filter { field.stamp(it) != null }) {
            val clamped = clampedStamp(label, limit, field)
            val best = newest[label.id]
            if (best == null || wins(clamped, best, field)) {
                newest[label.id] = clamped
            }
        }

        return merged.map { item ->
            val source = newest[item.id]
            if (!item.label.isLive || source == null) {
                item
            } else if (field.text(item.label) == field.text(source) && sameStamp(field.stamp(item.label), field.stamp(source))) {
                item
            } else {
                item.copy(label = field.take(item.label, source))
            }
        }
    }

    // A live label's device list in its stored form (no repeat, id order, none is null), whichever way it arrived.
    private fun withStoredOnlyOn(item: SyncTaskLabel): SyncTaskLabel {
        val onlyOn = item.label.onlyOn
        if (!item.label.isLive || onlyOn == null) return item
        val stored = TaskScopes.normalized(onlyOn)
        return if (stored != null && stored == onlyOn) item else item.copy(label = item.label.copy(onlyOn = stored))
    }

    // Both stamped: the later time, then the device id, then the list, so the choice never depends on the order.
    private fun wins(candidate: TaskLabel, held: TaskLabel, field: Field): Boolean {
        val first = field.stamp(candidate)!!
        val second = field.stamp(held)!!
        val byTime = DotNetTime.compare(first.at, second.at)
        if (byTime != 0) {
            return byTime > 0
        }

        val byDevice = first.by.compareTo(second.by)
        if (byDevice != 0) {
            return byDevice > 0
        }

        return field.text(candidate) > field.text(held)
    }

    // The PC compares the two stamps as records: the same moment and device; a stamp carrying bases never equals another.
    private fun sameStamp(first: FieldStamp?, second: FieldStamp?): Boolean {
        if (first == null || second == null) {
            return first == null && second == null
        }

        return DotNetTime.same(first.at, second.at) && first.by == second.by && first.bases == null && second.bases == null
    }

    private fun clampedStamp(label: TaskLabel, limit: OffsetDateTime, field: Field): TaskLabel {
        val stamp = field.stamp(label)
        return if (stamp != null && DotNetTime.compare(stamp.at, limit) > 0) field.clamp(label, FieldStamp(limit, stamp.by)) else label
    }

    // The shared merge's future clamp, done here so the clamped time keeps the receiver's offset as on the PC.
    private fun clampedItem(item: SyncTaskLabel, limit: OffsetDateTime): SyncTaskLabel {
        val label = item.label
        return item.copy(label = label.copy(editedAt = DotNetTime.atMost(label.editedAt, limit), deletedAt = label.deletedAt?.let { DotNetTime.atMost(it, limit) }))
    }
}
