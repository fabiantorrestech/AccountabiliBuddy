package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import java.util.concurrent.ConcurrentHashMap

/**
 * "Add to Today" while a PC runs a session: the Later item or come-back is sent to the PC's checklist as a `taskAdd` action,
 * and it leaves its list here only once the PC's answer says it took the task (it keeps 3 and parks a 4th, so either way the
 * text is on the PC). A refusal leaves the item where it was, so nothing is lost when the PC says no (the last minute of a
 * phase, a session that just ended). Held in memory only: if the app is closed before the answer, the item simply stays.
 */
class TodayHandoff(private val lists: SharedListsStore) {
    /** Where a sent task came from. */
    sealed interface Source {
        data class Later(val id: String) : Source
        data class ComeBack(val id: String) : Source
    }

    private val waiting = ConcurrentHashMap<String, Source>()

    /** The `taskAdd` for [taskId] is on its way; when it is answered well, [source] leaves its list. */
    fun hold(taskId: String, source: Source) {
        waiting[taskId] = source
    }

    /** A PC answered [action] ([result] is null from an older PC that acks without answering, which is not taken as a yes). */
    fun answered(action: SyncAction, result: SyncActionResult?) {
        if (action.kind != SyncAction.TASK_ADD) return
        val source = action.item?.let { waiting.remove(it) } ?: return
        if (result?.ok != true) return
        when (source) {
            is Source.Later -> lists.deleteLater(source.id)
            is Source.ComeBack -> lists.tickParked(source.id)
        }
    }
}
