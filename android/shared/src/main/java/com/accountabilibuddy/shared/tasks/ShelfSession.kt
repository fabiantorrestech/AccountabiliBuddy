package com.accountabilibuddy.shared.tasks

import java.time.OffsetDateTime

/**
 * "This session is full" on the phone (round 2 R7.3b; bundle spec §2; the PC's `SessionFullViewModel` before a session starts):
 * This session holds [TaskLocalEdits.SESSION_ACTIONS] actions. A task that finds no room is offered a swap (one of This session's
 * tasks goes to Queued and the newcomer takes its place) or "Add anyway" (over the cap, with a reason kept on this phone, as "Keep
 * it to 3" always asked). Closing the offer leaves it where it was. Pure; while a PC's session runs This session is that PC's
 * checklist and none of this applies (the task is asked of the PC).
 */
object ShelfSession {
    /** This session's open tasks, in their order. */
    fun open(lists: TaskLists): List<TaskItem> =
        lists.tasks.map { it.task }.filter { it.isLive && it.bin == TaskBin.SESSION && !it.isDone }.sortedBy { it.position }

    /** The tasks among [ids] that This session has no room for, taking them in one after the other. */
    fun needsRoom(lists: TaskLists, ids: List<String>, sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS): List<String> {
        var free = ShelfEdits.free(lists.tasks, sessionActions)
        val waiting = ArrayList<String>()
        for (id in ids.map { it.lowercase() }.distinct()) {
            val task = live(lists, id) ?: continue
            if (task.bin == TaskBin.SESSION && !task.isDone) continue
            val needs = task.actionCount.coerceAtLeast(1)
            if (free >= needs) free -= needs else waiting += task.id
        }

        return waiting
    }

    /**
     * The tasks of This session a swap can send to Queued to let [incomingId] in: those whose places, once free, leave room for the
     * newcomer's actions (the PC's `DraftSwapAdmits`). None: only "Add anyway" is left.
     */
    fun slots(lists: TaskLists, incomingId: String, sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS): List<TaskItem> {
        val incoming = live(lists, incomingId) ?: return emptyList()
        val free = ShelfEdits.free(lists.tasks, sessionActions, incoming.id)
        val needs = incoming.actionCount.coerceAtLeast(1)
        return open(lists).filter { !it.id.equals(incoming.id, ignoreCase = true) && free + it.actionCount >= needs }
    }

    /**
     * The swap: [outgoingId] goes to the end of Queued and [incomingId] takes its place in This session's order. Nothing changes
     * when the swap doesn't make room (the lists changed meanwhile). [ShelfChange.before] holds both tasks as they were, so Undo
     * ([ShelfEdits.restore] with [ShelfEdits.MOVE_FIELDS]) restores both.
     */
    fun swap(
        lists: TaskLists, incomingId: String, outgoingId: String, self: String, now: OffsetDateTime,
        sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS,
    ): ShelfChange {
        val none = ShelfChange(lists, emptyList(), emptyList())
        val incoming = live(lists, incomingId) ?: return none
        val outgoing = slots(lists, incomingId, sessionActions).firstOrNull { it.id.equals(outgoingId, ignoreCase = true) } ?: return none
        if (incoming.bin == TaskBin.SESSION && !incoming.isDone) return none
        val out = ShelfEdits.move(lists, listOf(outgoing.id), TaskBin.QUEUED, self, now, sessionActions)
        val into = ShelfEdits.move(out.lists, listOf(incoming.id), TaskBin.SESSION, self, now, sessionActions)
        if (!out.changed || !into.changed || into.queuedInstead > 0) return none
        // The newcomer takes the place the other one left.
        val placed = ShelfEdits.edit(into.lists, incoming.id, TaskField.BIN, self, now) { it.copy(position = outgoing.position) }
        return ShelfChange(placed.lists, listOf(incoming.id, outgoing.id), listOf(incoming, outgoing))
    }

    /** "Add anyway": into This session over the cap. The caller has kept the reason first; without one this is never called. */
    fun addAnyway(lists: TaskLists, ids: List<String>, self: String, now: OffsetDateTime): ShelfChange =
        ShelfEdits.move(lists, ids, TaskBin.SESSION, self, now, Int.MAX_VALUE)

    /** The toast after a swap: "Swapped · “Fix the tray icon” → Queued". */
    fun swappedText(outgoingTitle: String): String = "Swapped · “${ShelfToasts.short(outgoingTitle)}” → Queued"

    private fun live(lists: TaskLists, id: String): TaskItem? = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task
}
