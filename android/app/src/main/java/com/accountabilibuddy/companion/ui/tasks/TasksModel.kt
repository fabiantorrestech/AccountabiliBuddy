package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.companion.sync.LaterDone
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.TaskCategoryChoices
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAction
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
 * @property categories Every category seen on a task, come-back or Later item, for the filter chips and the category menu.
 * @property sessionTotal How many tasks This session holds before the chip and the search (for its empty text).
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
) {
    companion object {
        /**
         * @param pc The PC with a running session, or null: This session is then the next session's list on this phone.
         * @param comebacks The come-backs as Today reads them: the shared list, or an older PC's own.
         * @param parked The shared come-back list (live items).
         * @param category The chip picked (a category's name); null shows everything.
         * @param search What the user typed in the search; it matches titles, steps and notes.
         */
        fun build(
            nextTasks: List<SyncNextTask>,
            pc: TasksPc?,
            pending: List<SyncAction>,
            comebacks: OpenComebacks,
            parked: List<SyncParked>,
            later: List<SyncLaterItem>,
            laterDone: List<LaterDone>,
            received: List<ReceivedDevice>,
            category: String?,
            search: String,
            now: Instant,
            zone: ZoneId = ZoneId.systemDefault(),
        ): TasksModel {
            val today = now.atZone(zone).toLocalDate()
            val filter = TaskFilter(category, search)
            fun stepTitles(steps: List<SessionTaskItem>?) = steps.orEmpty().map { it.title }.toTypedArray()
            fun isToday(time: Instant) = time.atZone(zone).toLocalDate() == today

            val session = mutableListOf<TaskRowModel>()
            val done = mutableListOf<Pair<Instant, TaskRowModel>>()
            var sessionTotal = 0
            if (pc != null) {
                val open = SessionTaskItem.openInShownOrder(pc.checklist.tasks)
                val nowId = SessionTaskItem.shown(pc.checklist.tasks, pc.checklist.currentId)?.first?.id
                sessionTotal = open.size
                open.forEachIndexed { index, task ->
                    val id = task.id ?: return@forEachIndexed
                    if (!filter.allows(task.category, task.title, *stepTitles(task.subtasks))) return@forEachIndexed
                    val sending = pending.any { it.target == pc.pcId && it.item == id && it.kind == SyncAction.TICK_TASK }
                    session += TaskRowModel(
                        TaskRef(TaskKind.Running, id), task.title, TaskText.line(TaskText.stepsLeft(task.subtasks)), task.category, task.categoryColor,
                        number = index + 1, done = sending, isNow = id == nowId, sending = sending,
                    )
                }
                pc.checklist.tasks.filter { it.isDone && it.id != null }.forEach { task ->
                    if (!filter.allows(task.category, task.title, *stepTitles(task.subtasks))) return@forEach
                    val at = task.doneAt!!
                    done += at to TaskRowModel(
                        TaskRef(TaskKind.DoneRunning, task.id!!), task.title, "Finished ${TaskText.clock(at, zone)}", task.category, task.categoryColor, done = true,
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
                        task.category, task.categoryColor, number = index + 1,
                    )
                }
                nextTasks.filter { it.isDone && it.doneAt?.let(::isToday) == true }.forEach { task ->
                    if (!filter.allows(task.category, task.title, task.note, *stepTitles(task.subtasks))) return@forEach
                    val at = task.doneAt!!
                    done += at to TaskRowModel(
                        TaskRef(TaskKind.DoneNext, task.id), task.title, "Finished ${TaskText.clock(at, zone)}", task.category, task.categoryColor, done = true,
                    )
                }
            }

            val laterSorted = later.sortedWith(compareBy({ if (it.dueOn == null) 1 else 0 }, { it.dueOn.orEmpty() }))
            val laterRows = laterSorted.filter { item ->
                filter.allows(item.category, item.title, item.note, *stepTitles(item.subtasks))
            }.map { item ->
                TaskRowModel(TaskRef(TaskKind.Later, item.id), item.title, laterLine(item, today), item.category, item.categoryColor)
            }
            laterDone.filter { isToday(it.doneAt) }.forEach { entry ->
                val item = entry.item
                if (!filter.allows(item.category, item.title, item.note, *stepTitles(item.subtasks))) return@forEach
                done += entry.doneAt to TaskRowModel(
                    TaskRef(TaskKind.DoneLater, item.id), item.title, "Finished ${TaskText.clock(entry.doneAt, zone)}", item.category, item.categoryColor, done = true,
                )
            }

            val categories = (
                TaskCategoryChoices.forNextTasks(nextTasks, received) +
                    later.mapNotNull { item -> item.category?.let { Choice(it, item.categoryColor) } } +
                    parked.mapNotNull { item -> item.category?.let { Choice(it, item.categoryColor) } } +
                    pc?.checklist?.tasks?.let(TaskCategoryChoices::forChecklist).orEmpty()
                ).distinctBy { it.name.lowercase() }.sortedBy { it.name.lowercase() }

            return TasksModel(
                session = session,
                comeBacks = comebackRows(comebacks, parked, pending, received, today, zone, filter),
                later = laterRows,
                laterTotal = later.size,
                nextDue = nextDue(later, today),
                doneToday = done.sortedByDescending { it.first }.map { it.second },
                categories = categories,
                sessionTotal = sessionTotal,
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
            today: LocalDate,
            zone: ZoneId,
            filter: TaskFilter,
        ): List<TaskRowModel> {
            val oldPc = comebacks.legacySource
            if (oldPc != null) {
                // An older PC's own come-backs: they can be ticked (an action for that PC) and nothing else.
                val target = deviceIdOf(oldPc, received)
                return comebacks.items.filter { filter.allows(null, it.text) }.map { row ->
                    val sending = target != null && pending.any { it.target == target && it.item == row.id && it.kind == SyncAction.TICK_COMEBACK }
                    TaskRowModel(TaskRef(TaskKind.ComeBack, row.id), row.text, "", null, null, done = sending, sending = sending, readOnly = true)
                }
            }

            return parked.filter { it.deletedAt == null && it.doneAt == null && filter.allows(it.category, it.text) }.map { item ->
                TaskRowModel(
                    TaskRef(TaskKind.ComeBack, item.id), item.text, "Parked ${TaskText.whenText(item.createdAt, today, zone)}", item.category, item.categoryColor,
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
