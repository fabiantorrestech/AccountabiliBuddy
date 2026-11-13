package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Merges two label lists (ADR 76; the PC's TaskLabelMerge): item by item, newest version first ([SharedListMerge]), then each live
 * label's "Rings on" on its own: the copy with the newest [TaskLabel.ringOnStamp] gives it (a stamped list beats an unstamped one;
 * between two stamps the later time, then the device id, then the list; a stamp far ahead counts as the future limit ahead). So a
 * newer change to the label's name or colour made without the newest rings on never takes the choice back, and every device picks
 * the same whatever order the lists come in. Label tombstones are kept for good (ids and stamps only, no text): a tombstone is what
 * stops an older phone's category bringing a deleted label back.
 */
object TaskLabelMerge {
    fun merge(held: List<SyncTaskLabel>, incoming: List<SyncTaskLabel>, now: OffsetDateTime): List<SyncTaskLabel> {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        val merged = SharedListMerge.merge(held.map { clampedItem(it, limit) }, incoming.map { clampedItem(it, limit) }, now.toInstant(), keepTombstones = true)

        val newest = HashMap<String, TaskLabel>()
        for (label in (held + incoming).map { it.label }.filter { it.isLive && it.ringOnStamp != null }) {
            val clamped = clampedRing(label, limit)
            val best = newest[label.id]
            if (best == null || wins(clamped, best)) {
                newest[label.id] = clamped
            }
        }

        return merged.map { item ->
            val source = newest[item.id]
            if (!item.label.isLive || source == null) {
                item
            } else if (TaskRingDevices.same(item.label.ringOn, source.ringOn) && sameStamp(item.label.ringOnStamp, source.ringOnStamp)) {
                item
            } else {
                item.copy(label = item.label.copy(ringOn = source.ringOn, ringOnStamp = source.ringOnStamp))
            }
        }
    }

    // Both stamped: the later time, then the device id, then the list, so the choice never depends on the order.
    private fun wins(candidate: TaskLabel, held: TaskLabel): Boolean {
        val first = candidate.ringOnStamp!!
        val second = held.ringOnStamp!!
        val byTime = DotNetTime.compare(first.at, second.at)
        if (byTime != 0) {
            return byTime > 0
        }

        val byDevice = first.by.compareTo(second.by)
        if (byDevice != 0) {
            return byDevice > 0
        }

        return TaskRingDevices.text(candidate.ringOn) > TaskRingDevices.text(held.ringOn)
    }

    // The PC compares the two stamps as records: the same moment and device; a stamp carrying bases never equals another.
    private fun sameStamp(first: FieldStamp?, second: FieldStamp?): Boolean {
        if (first == null || second == null) {
            return first == null && second == null
        }

        return DotNetTime.same(first.at, second.at) && first.by == second.by && first.bases == null && second.bases == null
    }

    private fun clampedRing(label: TaskLabel, limit: OffsetDateTime): TaskLabel {
        val stamp = label.ringOnStamp
        return if (stamp != null && DotNetTime.compare(stamp.at, limit) > 0) label.copy(ringOnStamp = FieldStamp(limit, stamp.by)) else label
    }

    // The shared merge's future clamp, done here so the clamped time keeps the receiver's offset as on the PC.
    private fun clampedItem(item: SyncTaskLabel, limit: OffsetDateTime): SyncTaskLabel {
        val label = item.label
        return item.copy(label = label.copy(editedAt = DotNetTime.atMost(label.editedAt, limit), deletedAt = label.deletedAt?.let { DotNetTime.atMost(it, limit) }))
    }
}
