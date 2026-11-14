package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Another device's (or a backup's) task list merged in (the PC's TaskListRules.MergeLists): tasks field by field
 * ([TaskFieldMerge]), labels item by item with "Rings on" by its own stamp ([TaskLabelMerge]), views and History item by item
 * (newest version wins, tombstones 30 days, the 2-minute future clamp), the account's Tasks settings field by field (none incoming
 * keeps this device's). The same result whichever list comes first.
 */
object TaskListMerge {
    fun mergeLists(lists: TaskLists, incoming: TaskLists, self: String, now: OffsetDateTime): TaskLists = TaskLists(
        TaskFieldMerge.merge(lists.tasks, incoming.tasks, now, self),
        TaskLabelMerge.merge(lists.labels, incoming.labels, now),
        views(lists.views, incoming.views, now),
        taskLog(lists.log, incoming.log, now),
        if (incoming.account == null) lists.account else TaskAccountSettingsMerge.merge(lists.account, incoming.account, now),
        if (incoming.tagScopes == null) lists.tagScopes else TagScopeMerge.merge(lists.tagScopes, TaskScopes.clean(incoming.tagScopes), now),
    )

    fun views(held: List<SyncTaskView>, incoming: List<SyncTaskView>, now: OffsetDateTime): List<SyncTaskView> {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        return SharedListMerge.merge(held.map { clamped(it, limit) }, incoming.map { clamped(it, limit) }, now.toInstant())
    }

    fun taskLog(held: List<SyncTaskLogItem>, incoming: List<SyncTaskLogItem>, now: OffsetDateTime): List<SyncTaskLogItem> {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        return SharedListMerge.merge(held.map { clamped(it, limit) }, incoming.map { clamped(it, limit) }, now.toInstant())
    }

    // The shared merge's future clamp, done here so the clamped time keeps the receiver's offset as on the PC.
    private fun clamped(item: SyncTaskView, limit: OffsetDateTime): SyncTaskView {
        val view = item.view
        return item.copy(view = view.copy(editedAt = DotNetTime.atMost(view.editedAt, limit), deletedAt = view.deletedAt?.let { DotNetTime.atMost(it, limit) }))
    }

    private fun clamped(item: SyncTaskLogItem, limit: OffsetDateTime): SyncTaskLogItem {
        val entry = item.entry
        return item.copy(entry = entry.copy(editedAt = DotNetTime.atMost(entry.editedAt, limit), deletedAt = entry.deletedAt?.let { DotNetTime.atMost(it, limit) }))
    }
}
