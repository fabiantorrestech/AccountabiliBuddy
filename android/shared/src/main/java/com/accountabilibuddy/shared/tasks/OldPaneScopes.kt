package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncHistoryTask
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked

/**
 * ADR 89 (LB1c): the phone's OLD task screens (the Tasks pane for tablets and the old lists) show categories by NAME and tags as plain words.
 * What goes to the other devices is untouched (the old fields still carry every label and tag); what this phone DRAWS drops the labels and
 * tags that are hidden here ([TaskScopeView]): a task whose only category is hidden reads as having none, and a hidden tag is left off
 * its words. The same rules as the new Tasks page, on names.
 */
object OldPaneScopes {
    private fun List<String>?.shown(view: TaskScopeView): List<String>? = view.tags(this)

    fun nextTasks(list: List<SyncNextTask>, view: TaskScopeView): List<SyncNextTask> =
        list.map { if (view.categoryShown(it.category) && view.tags(it.labels) == it.labels) it else it.copy(category = category(view, it.category), categoryColor = color(view, it.category, it.categoryColor), labels = it.labels.shown(view)) }

    fun later(list: List<SyncLaterItem>, view: TaskScopeView): List<SyncLaterItem> =
        list.map { if (view.categoryShown(it.category) && view.tags(it.labels) == it.labels) it else it.copy(category = category(view, it.category), categoryColor = color(view, it.category, it.categoryColor), labels = it.labels.shown(view)) }

    fun parked(list: List<SyncParked>, view: TaskScopeView): List<SyncParked> =
        list.map { if (view.categoryShown(it.category) && view.tags(it.labels) == it.labels) it else it.copy(category = category(view, it.category), categoryColor = color(view, it.category, it.categoryColor), labels = it.labels.shown(view)) }

    fun autoSaved(list: List<SyncAutoSavedItem>, view: TaskScopeView): List<SyncAutoSavedItem> =
        list.map { if (view.categoryShown(it.category) && view.tags(it.labels) == it.labels) it else it.copy(category = category(view, it.category), categoryColor = color(view, it.category, it.categoryColor), labels = it.labels.shown(view)) }

    fun history(task: SyncHistoryTask, view: TaskScopeView): SyncHistoryTask =
        if (view.categoryShown(task.category) && view.tags(task.labels) == task.labels) task
        else task.copy(category = category(view, task.category), categoryColor = color(view, task.category, task.categoryColor), labels = task.labels.shown(view))

    fun checklist(list: List<SessionTaskItem>, view: TaskScopeView): List<SessionTaskItem> =
        list.map { if (view.categoryShown(it.category)) it else it.copy(categoryId = null, category = null, categoryColor = null) }

    fun categories(list: List<SyncCategory>, view: TaskScopeView): List<SyncCategory> = list.filter { view.categoryShown(it.name) }

    private fun category(view: TaskScopeView, name: String?): String? = name?.takeIf { view.categoryShown(it) }

    private fun color(view: TaskScopeView, name: String?, color: String?): String? = if (view.categoryShown(name)) color else null
}
