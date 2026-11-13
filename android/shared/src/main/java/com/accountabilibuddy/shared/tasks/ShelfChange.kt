package com.accountabilibuddy.shared.tasks

/**
 * What a change made on the phone's Tasks page did ([ShelfEdits]): the new [lists], the tasks it touched ([ids]; none: nothing
 * changed), those tasks as they were [before] (what Undo puts back, [ShelfEdits.restore]) and how many asked for This session but
 * went to Queued because the session was full ([queuedInstead]).
 */
data class ShelfChange(val lists: TaskLists, val ids: List<String>, val before: List<TaskItem>, val queuedInstead: Int = 0) {
    val changed: Boolean get() = ids.isNotEmpty()
}
