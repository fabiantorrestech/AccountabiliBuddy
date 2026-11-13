package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.OldTaskList
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskEdits
import com.accountabilibuddy.shared.tasks.TaskFacadeLists
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldMerge
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskMigrationCounts
import com.accountabilibuddy.shared.tasks.TaskMigrationResult
import com.accountabilibuddy.shared.tasks.TaskModelSwitch
import com.accountabilibuddy.shared.tasks.TaskNewerEntries
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskOrigins
import com.accountabilibuddy.shared.tasks.TaskParts
import com.accountabilibuddy.shared.tasks.TaskReminder
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * R7.2 checks with no C# twin: the phone's own edits through its old lists' views ([TaskLocalEdits]: stamps by this phone, ids,
 * numbers and fields the old lists can't carry kept), its label edits, the one-time switch's refusal when counts don't balance (the
 * mutation target), a newer app's entries replaced by id, and how long a field merge of thousands of tasks takes (R7.1 review).
 */
object TaskStoreSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private val zone: ZoneId = ZoneId.of("America/New_York")
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-12T17:00:00Z")

    private fun id(n: Int) = "79797979-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, bin: TaskBin, position: Double, number: Int, label: String? = null): SyncTaskItem {
        val created = OffsetDateTime.parse("2026-11-10T13:00:00Z")
        val item = TaskItem(
            id(n), "Task $n", bin, position, created, created, created, number = TaskNumber(number), labelId = label,
            reminders = if (n == 1) listOf(TaskReminder(id(90), TaskReminder.AT_START)) else null, highlight = n == 1,
        )
        return SyncTaskItem(item, TaskFieldStamps.allAt(FieldStamp(created, PC)))
    }

    private fun world(): TaskLists {
        val work = TaskLabel(TaskLabel.idFor("Work"), "Work", "#2563EB", 1.0, OffsetDateTime.parse("2026-11-10T13:00:00Z"))
        return TaskLists(
            listOf(task(1, TaskBin.SESSION, 1.0, 1, work.id), task(2, TaskBin.SESSION, 2.0, 2), task(3, TaskBin.QUEUED, 1.0, 3), task(4, TaskBin.LATER, 1.0, 4, work.id), task(5, TaskBin.SORT, 1.0, 5)),
            listOf(SyncTaskLabel(work)), emptyList(), emptyList(),
        )
    }

    private fun none() = OldTaskFields(emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), emptyList())

    private fun normalized(lists: TaskLists) = TaskListRules.normalize(lists, PHONE, PC, now, zone)

    fun run(report: CheckReport) {
        localEdits(report)
        labels(report)
        switchRefuses(report)
        newerEntries(report)
        receive(report)
        timing(report)
    }

    private fun localEdits(report: CheckReport) {
        val world = world()
        val draft = TaskFacadeLists.draft(world, now)
        val later = TaskFacadeLists.later(world, now)

        // Edit a title through the old list's view: stamped by the phone, nothing else touched.
        val one = draft.first { it.id == id(1) }
        val renamed = TaskLocalEdits.apply(world, none().copy(nextTasks = listOf(one.copy(title = "Renamed on the phone"))), PHONE, now, zone)
        val edited = renamed.tasks.first { it.id == id(1) }
        report.check(
            "r72-self: an edit through the old list is a stamped title edit by the phone; reminders, highlight and number stay",
            edited.task.title == "Renamed on the phone" && edited.stamps.of(TaskField.TITLE)?.by == PHONE && edited.stamps.of(TaskField.BIN)?.by == PC &&
                edited.task.reminders == world.tasks[0].task.reminders && edited.task.highlight && edited.task.number == TaskNumber(1),
        )

        // A new task in the next session's list: This session while it fits, else Queued; origin "Added from phone".
        val added = TaskLocalEdits.apply(world, none().copy(nextTasks = listOf(one.copy(id = id(10), title = "New", position = 10.0))), PHONE, now, zone)
        val made = added.tasks.first { it.id == id(10) }.task
        report.check("r72-self: a new task fills This session's free place, made by the phone", made.bin == TaskBin.SESSION && made.origin == TaskOrigins.FROM_PHONE && added.tasks.first { it.id == id(10) }.stamps.of(TaskField.TITLE)?.by == PHONE)
        val full = TaskLocalEdits.apply(added, none().copy(nextTasks = listOf(one.copy(id = id(11), title = "Fourth", position = 11.0))), PHONE, now, zone)
        report.check("r72-self: with This session full, a new task goes to Queued (the cap in actions)", full.tasks.first { it.id == id(11) }.task.bin == TaskBin.QUEUED)

        // Tick a Later item: History; untick it: back in Later with its id and number.
        val laterItem = later.first { it.id == id(4) }
        val ticked = normalized(TaskLocalEdits.apply(world, none().copy(later = listOf(laterItem.copy(doneAt = now.toInstant()))), PHONE, now, zone))
        val shownTicked = TaskFacadeLists.later(ticked, now).first { it.id == id(4) }
        report.check("r72-self: a ticked Later item goes to History and still shows ticked in Later", ticked.log.any { it.id == id(4) && it.entry.kind == TaskHistoryKind.FINISHED } && shownTicked.doneAt != null && ticked.tasks.none { it.id == id(4) && it.task.isLive })
        val unticked = normalized(TaskLocalEdits.apply(ticked, none().copy(later = listOf(shownTicked.copy(doneAt = null))), PHONE, now.plusMinutes(1), zone))
        val back = unticked.tasks.first { it.id == id(4) }.task
        report.check("r72-self: unticking it brings the same task back to Later with its number and label; History lets it go", back.isLive && back.bin == TaskBin.LATER && back.number == TaskNumber(4) && back.labelId == world.tasks[3].task.labelId && unticked.log.none { it.id == id(4) && it.entry.isLive })

        // Delete and Undo: History "deleted", then the same task back whole.
        val deleted = normalized(TaskLocalEdits.apply(world, none().copy(nextTasks = listOf(one.copy(deletedAt = now.toInstant(), title = ""))), PHONE, now, zone))
        report.check("r72-self: a delete files the task in History as deleted and leaves a tombstone", deleted.log.any { it.id == id(1) && it.entry.kind == TaskHistoryKind.DELETED } && deleted.tasks.any { it.id == id(1) && !it.task.isLive })
        val restored = normalized(TaskLocalEdits.apply(deleted, none().copy(nextTasks = listOf(one)), PHONE, now.plusMinutes(1), zone))
        val again = restored.tasks.first { it.id == id(1) }.task
        report.check("r72-self: Undo brings the deleted task back whole (number, reminders) and History lets it go", again.isLive && again.number == TaskNumber(1) && again.reminders != null && restored.log.none { it.id == id(1) && it.entry.isLive })

        // A move keeps the task; the old list's tombstone in the same change is not a delete.
        val moved = TaskLocalEdits.move(world, id(4), OldTaskList.NEXT_TASKS, PHONE, now, zone)
        val movedTask = moved.tasks.first { it.id == id(4) }
        report.check("r72-self: a move keeps the id, number and label, a bin edit by the phone (This session has a free place)", movedTask.task.bin == TaskBin.SESSION && movedTask.task.number == TaskNumber(4) && movedTask.stamps.of(TaskField.BIN)?.by == PHONE && movedTask.stamps.of(TaskField.TITLE)?.by == PC)
        val departure = TaskLocalEdits.apply(moved, none().copy(later = listOf(laterItem.copy(deletedAt = now.toInstant(), title = ""))), PHONE, now, zone)
        report.check("r72-self: the other half of a move (a tombstone in the list it left) is not a delete", departure.tasks.first { it.id == id(4) }.task.isLive && departure.log.isEmpty())

        // Nothing changed: nothing stamped.
        val same = TaskLocalEdits.apply(world, none().copy(nextTasks = draft, later = later), PHONE, now, zone)
        report.check("r72-self: the views given back unchanged change nothing", same == world)
    }

    private fun labels(report: CheckReport) {
        val world = world()
        val renamed = TaskLabelEdits.rename(world, "work", "Job", now)
        report.check("r72-self: a renamed label keeps its id, so its tasks keep it", renamed.labels.single().label.name == "Job" && renamed.tasks.first { it.id == id(1) }.task.labelId == TaskLabel.idFor("Work"))
        report.check("r72-self: a name another label has is refused", TaskLabelEdits.rename(TaskLabelEdits.add(world, "Home", null, now, PHONE), "Work", "home", now).labels.map { it.label.name }.sorted() == listOf("Home", "Work"))
        val deleted = TaskLabelEdits.delete(world, "WORK", now, PHONE)
        report.check("r72-self: deleting a label leaves its tasks with No label (stamped by the phone) and keeps a tombstone", deleted.tasks.none { it.task.labelId != null } && deleted.tasks.first { it.id == id(1) }.stamps.of(TaskField.LABEL)?.by == PHONE && !deleted.labels.single().label.isLive)
        val revived = TaskLabelEdits.add(deleted, "Work", "#16A34A", now.plusMinutes(1), PHONE)
        report.check("r72-self: the name added again revives the label under its id, ringing everywhere", revived.labels.single().label.let { it.isLive && it.id == TaskLabel.idFor("Work") && it.ringOn == null && it.ringOnStamp?.by == PHONE })
    }

    private fun switchRefuses(report: CheckReport) {
        val counts = TaskMigrationCounts(mapOf("nextTasks" to 3), emptyMap(), mapOf(TaskBin.SESSION to 2), 0, 0, 0, 0, 0, 0, 3, 0)
        val result = TaskMigrationResult(world().tasks, world().labels, emptyList(), emptyList(), emptyMap(), counts)
        val refused = TaskModelSwitch.decide(result, PHONE, PC, now, zone)
        report.check("r72-self: counts that don't balance are refused: nothing to write, the phone stays on its old lists", refused.lists == null && refused.logLine.contains("balanced=false"), refused.logLine)
        val balanced = TaskModelSwitch.decide(result.copy(counts = counts.copy(bins = mapOf(TaskBin.SESSION to 3))), PHONE, PC, now, zone)
        report.check("r72-self: balanced counts are kept, normalized as the store keeps them", balanced.lists != null && balanced.lists == normalized(TaskLists.from(result)))
        report.check("r72-self: the log line holds counts only, never text", !refused.logLine.contains("Task "))
    }

    private fun newerEntries(report: CheckReport) {
        val newer = Json.parseToJsonElement("""{"task":{"id":"${id(50).uppercase()}","title":"t","bin":"someday"},"stamps":{}}""").jsonObject
        val held = TaskParts(null, null, null, null, null, newerTasks = listOf(newer))
        val tombstone = task(50, TaskBin.LATER, 1.0, 50).deleted(now)
        val replaced = TaskNewerEntries.merge(held, TaskParts(listOf(tombstone), null, null, null, null))
        report.check("r72-self (review): a newer app's entry goes when a file brings that id (a tombstone too)", replaced.newerTasks.isEmpty())
        val newerAgain = Json.parseToJsonElement("""{"task":{"id":"${id(50)}","title":"t2","bin":"someday"},"stamps":{}}""").jsonObject
        val swapped = TaskNewerEntries.merge(held, TaskParts(emptyList(), null, null, null, null, newerTasks = listOf(newerAgain)))
        report.check("r72-self (review): a newer app's entry is replaced by the file's version of it", swapped.newerTasks == listOf(newerAgain))
        report.check("r72-self (review): a file without the part leaves a newer app's entries alone", TaskNewerEntries.merge(held, TaskParts(null, null, null, null, null)).newerTasks == listOf(newer))
    }

    private fun receive(report: CheckReport) {
        val world = world()
        report.check("r72-self: no parts at all: nothing to merge", TaskListRules.mergeFile(world, null, PHONE, now) == null)
        report.check("r72-self: parts without tasks: the tasks stay (a missing part is never an empty one)", TaskListRules.mergeFile(world, TaskParts(null, emptyList(), emptyList(), emptyList(), null), PHONE, now) == null)
        val emptied = TaskListRules.mergeFile(world, TaskParts(emptyList(), null, null, null, null), PHONE, now)!!
        report.check("r72-self: an empty tasks part merges as one and deletes nothing", emptied.tasks.count { it.task.isLive } == world.tasks.size)
    }

    // R7.1 review item 1: a field merge of 2k and 5k tasks, both devices having edited every task.
    private fun timing(report: CheckReport) {
        for (size in listOf(2_000, 5_000)) {
            val base = (1..size).map { task(it, TaskBin.LATER, it.toDouble(), it) }
            val mine = base.map { TaskEdits.edit(it, TaskField.TITLE, it.task.copy(title = it.task.title + " (phone)"), now, PHONE) }
            val theirs = base.map { TaskEdits.edit(it, TaskField.ESTIMATE, it.task.copy(estimateMinutes = 15), now.minusMinutes(5), PC) }
            TaskFieldMerge.merge(mine.take(200), theirs.take(200), now, PHONE)
            val started = System.nanoTime()
            val merged = TaskFieldMerge.merge(mine, theirs, now, PHONE)
            val millis = (System.nanoTime() - started) / 1_000_000
            println("TIMING  r72-self: field merge of $size tasks (both sides edited every task): $millis ms")
            report.check("r72-self (review): a field merge of $size tasks keeps both edits of each task ($millis ms)", merged.size == size && merged.all { it.task.title.endsWith("(phone)") && it.task.estimateMinutes == 15 })
        }

        // Q584 (R7.1 review item 5): a full tie (time, device, value) with different bases gives the same bases whichever copy comes first.
        val base = task(1, TaskBin.LATER, 1.0, 1)
        val stampAt = OffsetDateTime.parse("2026-11-11T10:00:00Z")
        fun withBases(vararg bases: String) = base.copy(stamps = base.stamps.with(TaskField.TITLE, FieldStamp(stampAt, PC, bases.toList())))
        val a = withBases("aaaa000000000000", "bbbb000000000000")
        val b = withBases("bbbb000000000000", "cccc000000000000")
        val forward = TaskFieldMerge.mergeItem(a, b).stamps.of(TaskField.TITLE)?.bases
        val swapped = TaskFieldMerge.mergeItem(b, a).stamps.of(TaskField.TITLE)?.bases
        report.check("r72-self (Q584): on a full tie the bases' order no longer depends on which copy came first", forward == swapped && forward == listOf("bbbb000000000000", "cccc000000000000", "aaaa000000000000"), "$forward / $swapped")
    }
}
