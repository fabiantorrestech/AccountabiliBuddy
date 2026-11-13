package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.tasks.OldTaskFieldsWriter
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskEdits
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldMerge
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelBook
import com.accountabilibuddy.shared.tasks.TaskListMerge
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskMigration
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPreset
import com.accountabilibuddy.shared.tasks.TaskNumbering
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * R7.1 checks with no C# twin: what the phone does beyond the PC (unknown fields and a newer app's entries kept through a store
 * round trip and a merge), the shared stamp helper, the phone-only numbering rule, the label book's revive (internal on the PC)
 * and the migration's own promises (the same result twice, nothing added by merging it with itself).
 */
object TaskModelSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val OTHER_PHONE = "0bbbbbbb-0000-0000-0000-00000000000b"
    private const val TASK = "11111111-0000-0000-0000-000000000001"

    private fun at(text: String): OffsetDateTime = OffsetDateTime.parse(text)

    private fun task(json: String): SyncTaskItem = TaskJson.readSyncTask(Json.parseToJsonElement(json))

    fun run(report: CheckReport) {
        stamps(report)
        unknowns(report)
        numbering(report)
        labelBook(report)
        migration(report)
    }

    private fun stamps(report: CheckReport) {
        val now = at("2026-11-12T12:00:00-05:00")
        report.check("r71-self: next stamp is now when nothing held is as late", FieldStamps.next(now, at("2026-11-12T11:59:59-05:00"), null) == now)
        val held = at("2026-11-12T18:00:00+01:00")
        report.check("r71-self: next stamp is one tick past a held time that isn't earlier, with its offset", FieldStamps.next(now, held) == held.plusNanos(100))
        val later = at("2026-11-12T13:00:00-05:00")
        report.check("r71-self: next stamp goes past the latest of several held times", FieldStamps.next(now, held, later, held) == later.plusNanos(100))
        val a = FieldStamp(now, PC)
        val b = FieldStamp(at("2026-11-12T17:00:00+00:00"), PHONE)
        report.check("r71-self: equal moments in different offsets tie; the later device id is newer", !FieldStamps.firstIsNewer(a, b, "x", "x") && FieldStamps.firstIsNewer(b, a, "x", "x"))
        report.check("r71-self: a stamped value beats an unstamped one; neither stamped is the second", FieldStamps.firstWins(a, null, "", "z") && !FieldStamps.firstWins(null, a, "z", "") && !FieldStamps.firstWins(null, null, "z", "a"))
        val limit = now.plusMinutes(2)
        report.check("r71-self: a stamp past the limit is clamped to it, bases kept", FieldStamps.clamped(FieldStamp(now.plusDays(1), PC, listOf("h")), limit) == FieldStamp(limit, PC, listOf("h")))
        report.check("r71-self: a time finer than a tick is cut", DotNetTime.cut(at("2026-11-12T12:00:00.123456789Z")) == at("2026-11-12T12:00:00.1234567Z"))
    }

    private fun unknowns(report: CheckReport) {
        fun copy(edited: String, extra: String, estimate: Int) = task(
            """{"task":{"id":"$TASK","title":"t","bin":"sort","position":1,"createdAt":"2026-11-10T08:00:00Z","editedAt":"$edited","touchedAt":"$edited","estimateMinutes":$estimate,"zFuture":"$extra"},"stamps":{"estimate":{"at":"$edited","by":"$PC"},"zField":{"at":"$edited","by":"$PC"}},"zItem":"$extra"}""",
        )

        val older = copy("2026-11-11T08:00:00Z", "old", 10)
        val newer = copy("2026-11-11T09:00:00Z", "new", 20)
        val now = at("2026-11-12T12:00:00Z")
        val forward = TaskJson.text(TaskJson.writeTasks(TaskFieldMerge.merge(listOf(older), listOf(newer), now)))
        val swapped = TaskJson.text(TaskJson.writeTasks(TaskFieldMerge.merge(listOf(newer), listOf(older), now)))
        report.check("r71-self: a merge keeps the unknown fields of the copy edited last, whichever list comes first", forward == swapped && forward.contains("\"zFuture\":\"new\"") && forward.contains("\"zItem\":\"new\"") && forward.contains("\"zField\""), forward)
        report.check("r71-self: unknown fields stay out of the PC's tie-break text", !older.tieBreak.contains("zFuture") && !older.tieBreak.contains("zItem") && !older.tieBreak.contains("zField"))

        // The store as the PC writes it (its raw bytes: known times straight, so "+00:00" unescaped); the phone must write the same.
        val storeText = """{"tasks":[{"task":{"id":"$TASK","title":"t","bin":"someday","position":1,"createdAt":"2026-11-10T08:00:00Z","editedAt":"2026-11-10T08:00:00Z","touchedAt":"2026-11-10T08:00:00Z"},"stamps":{}}],"labels":[],"views":[],"taskLog":[{"entry":{"id":"$TASK","kind":"merged","when":"2026-11-10T08:00:00Z","task":{"id":"$TASK","title":"t"},"editedAt":"2026-11-10T08:00:00Z"}}],"taskAccountSettings":{"defaultViewStamp":{"at":"2026-11-10T08:00:00+00:00","by":"$PC"},"zSetting":1}}"""
        val store = Json.parseToJsonElement(storeText).jsonObject
        val parts = TaskJson.readParts(store)
        report.check("r71-self: a newer app's entries are set aside, not used", parts.tasks?.isEmpty() == true && parts.newerTasks.size == 1 && parts.newerTaskLog.size == 1)
        report.check("r71-self: a store read and written keeps a newer app's entries and unknown fields as written, in order", TaskJson.text(TaskJson.writeParts(parts)) == storeText, TaskJson.text(TaskJson.writeParts(parts)))
        val numberText = """{"zA":1.0,"zB":1.50,"zC":0.1000000000000000055511,"zD":1e400,"zE":-0.0,"zF":2}"""
        report.check("r71-self (review): unknown numbers are written back exactly as they came", TaskJson.text(Json.parseToJsonElement(numberText)) == numberText, TaskJson.text(Json.parseToJsonElement(numberText)))
        val known = JsonObject(mapOf("a" to GoalJson.number(2.5), "b" to GoalJson.number(1e-5), "c" to GoalJson.number(3.0), "d" to GoalJson.number(1e20)))
        report.check("r71-self (review): numbers this app made still go as .NET writes them", TaskJson.text(known) == """{"a":2.5,"b":1E-05,"c":3,"d":1E+20}""", TaskJson.text(known))
        report.check("r71-self: a file without the parts reads them as absent and writes none", TaskJson.readParts(JsonObject(emptyMap())).let { it.tasks == null && it.accountSettings == null && TaskJson.writeParts(it).isEmpty() })

        val odd = Json.parseToJsonElement(
            """[{"task":{"id":"$TASK","title":"t","bin":"sort","steps":[{"id":"$TASK"}]},"stamps":{}},{"task":{"id":"$TASK","title":null,"bin":"sort","deletedAt":"2026-11-10T08:00:00Z"},"stamps":{}}]""",
        )
        val read = TaskJson.readTasks(odd)!!
        report.check("r71-self: a step without a title leaves its task out; a tombstone without a title has the empty one (the PC keeps both as null)", read.items.size == 1 && read.items[0].task.title == "" && !read.items[0].task.isLive && read.newer.isEmpty())
    }

    private fun numbering(report: CheckReport) {
        report.check("r71-self: a group without a PC numbers on its phone with the lowest id", TaskNumbering.numberingDevice(listOf(PHONE to "phone", OTHER_PHONE.uppercase() to "Phone", "cccccccc-0000-0000-0000-00000000000c" to "watch")) == OTHER_PHONE)
        report.check("r71-self: a PC numbers whatever the phones' ids", TaskNumbering.numberingDevice(listOf(OTHER_PHONE to "phone", PC.uppercase() to "pc")) == PC)
        report.check("r71-self: no PC and no phone: nobody numbers", TaskNumbering.numberingDevice(listOf(PHONE to "watch")) == null)
        val created = at("2026-11-10T08:00:00Z")
        val plain = SyncTaskItem(TaskItem(TASK, "t", TaskBin.SORT, 1.0, created, created, created), TaskFieldStamps.NONE)
        val numbered = TaskNumbering.assignFinal(listOf(plain), PHONE.uppercase(), PHONE, at("2026-11-12T12:00:00Z")).single()
        report.check("r71-self: the numbering phone gives a final number, stamped by it", numbered.task.number?.let { it.value == 1 && !it.temporary } == true && numbered.stamps.of(TaskField.NUMBER)?.by == PHONE)
    }

    private fun labelBook(report: CheckReport) {
        val deleted = at("2026-11-11T08:00:00Z")
        val tombstone = TaskLabel(TaskLabel.idFor("Work"), "Work", "#2F6BEA", 1.0, at("2026-11-10T08:00:00Z"), ringOn = listOf(PHONE), ringOnStamp = FieldStamp(at("2026-11-10T08:00:00Z"), PC)).deleted(deleted)
        val book = TaskLabelBook(listOf(tombstone), PHONE)
        val id = book.idForName(" work ", "teal", at("2026-11-10T09:00:00Z"))
        val revived = book.labels.single()
        report.check("r71-self: a name used again brings its deleted label back under the same id", id == tombstone.id && revived.isLive && revived.name == "work" && book.made == 1)
        report.check("r71-self: a revived label rings on every device, stamped one tick past the tombstone by the reviver", revived.ringOn == null && revived.ringOnStamp == FieldStamp(deleted.plusNanos(100), PHONE) && revived.editedAt == deleted.plusNanos(100))
        val renamed = TaskLabel(TaskLabel.idFor("Work"), "Job", "#2F6BEA", 1.0, deleted)
        report.check("r71-self: a live label renamed since never gives its id to its old name", TaskLabelBook.freeIdFor("Work", listOf(renamed)) == TaskLabel.idFor("Work#2"))
        report.check("r71-self: no name gives no label", book.idForName("  ", null, deleted) == null && book.idForName(null, null, deleted) == null)
    }

    private fun migration(report: CheckReport) {
        val edited = Instant.parse("2026-11-10T13:00:00Z")
        val input = TaskMigrationInput(
            listOf(SyncCategory("Work", "#2563EB")),
            listOf(
                SyncNextTask(TASK.uppercase(), "From an item's category", null, "Errands", "#EA580C", null, edited, null, 1.0),
                SyncNextTask("11111111-0000-0000-0000-000000000002", "Second", null, "Work", null, null, edited, null, 2.0),
            ),
            emptyList(), emptyList(), emptyList(),
            listOf(TaskMigrationPreset("99999999-0000-0000-0000-000000000001", "Errands only", "errands", "milk")),
            at("2026-11-12T17:00:00Z"), ZoneId.of("America/New_York"), isNumberingDevice = false,
        )
        val first = TaskMigration.run(input)
        val again = TaskMigration.run(input)
        report.check("r71-self: the migration gives the same result every time", first == again)
        report.check("r71-self: the migration's counts balance", first.counts.isBalanced && first.counts.sources.values.sum() == 2 && first.counts.bins[TaskBin.SORT] == 2)
        report.check("r71-self: MIG1: open next-session tasks land in To sort in order, with the earlier-version note, none in This session or Queued", first.tasks.filter { it.task.isLive }.sortedBy { it.position }.map { it.task.bin to it.task.origin } == List(2) { TaskBin.SORT to com.accountabilibuddy.shared.tasks.TaskOrigins.EARLIER_VERSION } && first.counts.bins[TaskBin.SESSION] == 0 && first.counts.bins[TaskBin.QUEUED] == 0)
        // MIG1 fix: a task last edited 45 days ago still leaves deletion markers in the old lists (its bin is stamped at the migration), so an older device sees it once.
        val stale = Instant.parse("2026-09-20T13:00:00Z")
        val staleRun = TaskMigration.run(input.copy(nextTasks = listOf(SyncNextTask("11111111-0000-0000-0000-000000000009", "Old next", null, null, null, null, stale, null, 1.0))))
        val staleTask = staleRun.tasks.single { it.task.isLive }
        val migratedAt = input.now
        report.check("r71-self: MIG1 fix: the migration stamps a task's bin at the migration time (the rest keeps its old edit time) and counts the move as an edit",
            staleTask.stamps.of(TaskField.BIN)?.at?.toInstant() == migratedAt.toInstant() && staleTask.stamps.of(TaskField.TITLE)?.at?.toInstant() == stale && staleTask.task.editedAt.toInstant() == migratedAt.toInstant())
        val staleWritten = OldTaskFieldsWriter.write(staleRun.tasks, staleRun.labels, staleRun.history, migratedAt.plusDays(1), ZoneId.of("America/New_York"))
        report.check("r71-self: MIG1 fix: the old lists carry deletion markers for the 45-day-old task in the next-session and Later lists, and the come-back live",
            staleWritten.nextTasks.any { it.id == staleTask.id && it.deletedAt != null } && staleWritten.later.any { it.id == staleTask.id && it.deletedAt != null }
                && staleWritten.parked.any { it.id == staleTask.id && it.deletedAt == null })
        report.check("r71-self: an old id in upper case becomes the PC's lower case", first.tasks.any { it.id == TASK })
        val view = first.views.first { it.view.name == "Errands only" }.view
        report.check("r71-self: a saved filter on a category only an item carried shows that label; its search words stay local", view.config.labels == listOf(TaskLabel.idFor("Errands")) && first.viewSearches[view.id] == "milk" && !TaskJson.text(TaskJson.writeViews(first.views)).contains("milk"))
        val lists = TaskLists.from(first)
        // R7.1 review: not a self-merge. A PC's later title edit of one task arrives; nothing else changes, and merging it again adds nothing.
        val now = at("2026-11-12T17:00:00Z")
        val target = lists.tasks.first { it.task.isLive }
        val editedItem = TaskEdits.edit(target, TaskField.TITLE, target.task.copy(title = "Edited on the PC"), at("2026-11-12T16:00:00Z"), PC)
        val incoming = lists.copy(tasks = lists.tasks.map { if (it.id == target.id) editedItem else it })
        val merged = TaskListMerge.mergeLists(lists, incoming, PHONE, now)
        fun others(held: TaskLists) = held.tasks.filter { it.id != target.id }.map { it.task }
        report.check("r71-self: a PC's later edit of a migrated task arrives, the rest stays as migrated", merged.tasks.first { it.id == target.id }.task.title == "Edited on the PC" && others(merged) == others(lists) && merged.labels == lists.labels && first == again)
        val twice = TaskListMerge.mergeLists(merged, incoming, PHONE, now)
        report.check("r71-self: the same file merged again adds nothing", TaskJson.text(TaskJson.writeTasks(twice.tasks)) == TaskJson.text(TaskJson.writeTasks(merged.tasks)))
    }
}
