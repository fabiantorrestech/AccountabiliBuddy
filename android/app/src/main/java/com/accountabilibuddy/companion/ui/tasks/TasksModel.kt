package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.TaskCategoryChoices
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.sync.TaskCategoryColors
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The Tasks pane's four groups as rows ready to draw, for one moment: This session, Come back to, Later and Done today, after
 * the category chip and the search. Built from the lists in memory only (the search text is never stored or sent).
 *
 * @property laterTotal How many Later items there are, whatever the chip and the search.
 * @property nextDue The Later header's "Next due: Expense report, Fri"; null when no item has a due date.
 * @property categories Every category this phone or another device lists, or seen on a task, come-back or Later item, in its current
 *   colour, for the filter chips and the category menu.
 * @property sessionTotal How many tasks This session holds before the chip and the search (for its empty text).
 * @property doneAllToday Every row of [doneToday] was ticked today (the group is then "Done today"; older ones make it "Done").
 */
class TasksModel(
    val session: List<TaskRowModel>,
    val comeBacks: List<TaskRowModel>,
    val later: List<TaskRowModel>,
    val laterTotal: Int,
    val nextDue: String?,
    val doneToday: List<TaskRowModel>,
    val categories: List<Choice>,
    val sessionTotal: Int,
    val doneAllToday: Boolean,
) {
    companion object {
        /**
         * @param pc The PC with a running session, or null: This session is then the next session's list on this phone.
         * @param comebacks The come-backs as Today reads them: the shared list, or an older PC's own.
         * @param parked The shared come-back list (live items).
         * @param later The Later list, live items: the open ones and the ticked ones (which show under Done).
         * @param waiting Ids of Later items and come-backs whose "Add to Today" waits for the PC's answer.
         * @param category The chip picked (a category's name); null shows everything.
         * @param search What the user typed in the search; it matches titles, steps and notes.
         * @param ownCategories This phone's own list of categories.
         */
        fun build(
            nextTasks: List<SyncNextTask>,
            pc: TasksPc?,
            pending: List<SyncAction>,
            comebacks: OpenComebacks,
            parked: List<SyncParked>,
            later: List<SyncLaterItem>,
            waiting: Set<String>,
            received: List<ReceivedDevice>,
            category: String?,
            search: String,
            now: Instant,
            zone: ZoneId = ZoneId.systemDefault(),
            ownCategories: List<SyncCategory> = emptyList(),
            /** ADR 71: false while this phone doesn't share its task lists: the other devices' categories stay out. */
            shareCategories: Boolean = true,
        ): TasksModel {
            val colors = TaskCategoryColors(ownCategories, if (shareCategories) received else emptyList(), now)
            val today = now.atZone(zone).toLocalDate()
            val filter = TaskFilter(category, search)
            fun stepTitles(steps: List<SessionTaskItem>?) = steps.orEmpty().map { it.title }.toTypedArray()
            fun isToday(time: Instant) = time.atZone(zone).toLocalDate() == today

            val session = mutableListOf<TaskRowModel>()
            val done = mutableListOf<Pair<Instant, TaskRowModel>>()
            // A done row says when: the time today, else the day and time ("Finished Mon 3:00 PM").
            fun finished(at: Instant) = "Finished ${TaskText.whenText(at, today, zone)}"
            var sessionTotal = 0
            if (pc != null) {
                val open = SessionTaskItem.openInShownOrder(pc.checklist.tasks)
                val nowId = SessionTaskItem.shown(pc.checklist.tasks, pc.checklist.currentId)?.first?.id
                sessionTotal = open.size
                open.forEachIndexed { index, task ->
                    // A task the PC sent with no id can't be ticked or edited from here: it is drawn, read-only.
                    val id = task.id.orEmpty()
                    if (!filter.allows(task.category, task.title, *stepTitles(task.subtasks))) return@forEachIndexed
                    val sending = id.isNotEmpty() && pending.any { it.target == pc.pcId && it.item == id && it.kind == SyncAction.TICK_TASK }
                    session += TaskRowModel(
                        TaskRef(TaskKind.Running, id), task.title, TaskText.line(TaskText.stepsLeft(task.subtasks)), task.category, colors.of(task.category, task.categoryColor),
                        number = index + 1, done = sending, isNow = id.isNotEmpty() && id == nowId, sending = sending, readOnly = id.isEmpty(),
                    )
                }
                pc.checklist.tasks.filter { it.isDone && it.id != null }.forEach { task ->
                    if (!filter.allows(task.category, task.title, *stepTitles(task.subtasks))) return@forEach
                    val at = task.doneAt!!
                    done += at to TaskRowModel(
                        TaskRef(TaskKind.DoneRunning, task.id!!), task.title, finished(at), task.category, colors.of(task.category, task.categoryColor), done = true,
                    )
                }
            } else {
                val open = groupedByCategory(nextTasks.filter { !it.isDone })
                sessionTotal = open.size
                open.forEachIndexed { index, task ->
                    if (!filter.allows(task.category, task.title, task.note, *stepTitles(task.subtasks))) return@forEachIndexed
                    session += TaskRowModel(
                        TaskRef(TaskKind.Next, task.id), task.title,
                        TaskText.line(TaskText.stepsLeft(task.subtasks), task.estimateMinutes?.let(TaskText::estimate), TaskText.planned(task.plannedFor, today)),
                        task.category, colors.of(task.category, task.categoryColor), number = index + 1,
                    )
                }
                // Every ticked task of the list shows, whatever its day: it still counts toward the 3 until a session takes the list.
                nextTasks.filter { it.isDone }.forEach { task ->
                    if (!filter.allows(task.category, task.title, task.note, *stepTitles(task.subtasks))) return@forEach
                    val at = task.doneAt!!
                    done += at to TaskRowModel(
                        TaskRef(TaskKind.DoneNext, task.id), task.title, finished(at), task.category, colors.of(task.category, task.categoryColor), done = true,
                    )
                }
            }

            val openLater = later.filter { it.doneAt == null }
            val laterSorted = openLater.sortedWith(compareBy({ if (it.dueOn == null) 1 else 0 }, { it.dueOn.orEmpty() }))
            val laterRows = laterSorted.filter { item ->
                filter.allows(item.category, item.title, item.note, *stepTitles(item.subtasks))
            }.map { item ->
                val sending = item.id in waiting
                TaskRowModel(
                    TaskRef(TaskKind.Later, item.id), item.title, TaskText.line(if (sending) "sent to the PC, waiting for its answer" else null, laterLine(item, today)),
                    item.category, colors.of(item.category, item.categoryColor), sending = sending,
                )
            }
            later.filter { it.doneAt != null }.forEach { item ->
                if (!filter.allows(item.category, item.title, item.note, *stepTitles(item.subtasks))) return@forEach
                val at = item.doneAt!!
                done += at to TaskRowModel(TaskRef(TaskKind.DoneLater, item.id), item.title, finished(at), item.category, colors.of(item.category, item.categoryColor), done = true)
            }

            val categories = (
                TaskCategoryChoices.forNextTasks(nextTasks, received) +
                    later.mapNotNull { item -> item.category?.let { Choice(it, item.categoryColor) } } +
                    parked.mapNotNull { item -> item.category?.let { Choice(it, item.categoryColor) } } +
                    pc?.checklist?.tasks?.let(TaskCategoryChoices::forChecklist).orEmpty() +
                    // Last, so a choice that knows the PC's id for it is the one kept.
                    colors.listed.map { Choice(it.name, it.color) }
                ).distinctBy { it.name.lowercase() }.sortedBy { it.name.lowercase() }
                .map { it.copy(color = colors.of(it.name, it.color)) }

            return TasksModel(
                session = session,
                comeBacks = comebackRows(comebacks, parked, pending, received, waiting, today, zone, filter, colors),
                later = laterRows,
                laterTotal = openLater.size,
                nextDue = nextDue(openLater, today),
                doneToday = done.sortedByDescending { it.first }.map { it.second },
                categories = categories,
                sessionTotal = sessionTotal,
                doneAllToday = done.all { isToday(it.first) },
            )
        }

        // This session's open tasks in the order the PC lists them: by category in the order each first appears, none last.
        private fun groupedByCategory(tasks: List<SyncNextTask>): List<SyncNextTask> {
            val named = tasks.filter { it.category != null }.groupBy { it.category!!.lowercase() }.values.flatten()
            return named + tasks.filter { it.category == null }
        }

        private fun comebackRows(
            comebacks: OpenComebacks,
            parked: List<SyncParked>,
            pending: List<SyncAction>,
            received: List<ReceivedDevice>,
            waiting: Set<String>,
            today: LocalDate,
            zone: ZoneId,
            filter: TaskFilter,
            colors: TaskCategoryColors,
        ): List<TaskRowModel> {
            val oldPc = comebacks.legacySource
            if (oldPc != null) {
                // An older PC's own come-backs: they can be ticked (an action for that PC) and nothing else.
                val target = deviceIdOf(oldPc, received)
                val own = comebacks.items.filter { filter.allows(null, it.text) }.map { row ->
                    val sending = target != null && pending.any { it.target == target && it.item == row.id && it.kind == SyncAction.TICK_COMEBACK }
                    TaskRowModel(TaskRef(TaskKind.ComeBack, row.id), row.text, "", null, null, done = sending, sending = sending, readOnly = true)
                }
                // What was parked here for that PC and it has not answered yet.
                val parkedHere = pending.filter { it.target == target && it.kind == SyncAction.PARK && it.text != null && filter.allows(null, it.text) }.map { action ->
                    TaskRowModel(TaskRef(TaskKind.ComeBack, action.id), action.text!!, "sending to $oldPc…", null, null, sending = true, readOnly = true)
                }
                return own + parkedHere
            }

            return parked.filter { it.deletedAt == null && it.doneAt == null && filter.allows(it.category, it.text) }.map { item ->
                val sending = item.id in waiting
                TaskRowModel(
                    TaskRef(TaskKind.ComeBack, item.id), item.text,
                    TaskText.line(if (sending) "sent to the PC, waiting for its answer" else null, "Parked ${TaskText.whenText(item.createdAt, today, zone)}"),
                    item.category, colors.of(item.category, item.categoryColor), sending = sending,
                )
            }
        }

        private fun laterLine(item: SyncLaterItem, today: LocalDate): String {
            val due = item.dueOn?.let { runCatching { LocalDate.parse(it) }.getOrNull() }?.let { "due ${TaskText.dayLabel(it, today)}" }
            val steps = item.subtasks?.size?.takeIf { it > 0 }?.let { if (it == 1) "1 step" else "$it steps" }
            return TaskText.line(due, steps, item.estimateMinutes?.let(TaskText::estimate), TaskText.planned(item.plannedFor, today))
        }

        private fun nextDue(later: List<SyncLaterItem>, today: LocalDate): String? {
            val dated = later.mapNotNull { item -> item.dueOn?.let { runCatching { LocalDate.parse(it) }.getOrNull() }?.let { it to item } }
            val next = dated.filter { it.first >= today }.minByOrNull { it.first } ?: dated.minByOrNull { it.first } ?: return null
            return "Next due: ${next.second.title}, ${TaskText.dayLabel(next.first, today)}"
        }
    }
}
