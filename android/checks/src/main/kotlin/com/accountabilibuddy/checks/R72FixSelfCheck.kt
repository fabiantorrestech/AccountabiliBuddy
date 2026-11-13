package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.TaskAddSent
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskFacadeLists
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskListUpdateNotice
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskMigration
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPort
import com.accountabilibuddy.shared.tasks.TaskModelStartup
import com.accountabilibuddy.shared.tasks.TaskModelSwitch
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskNumbering
import com.accountabilibuddy.shared.tasks.TaskOldStore
import com.accountabilibuddy.shared.tasks.TaskParts
import com.accountabilibuddy.shared.tasks.TaskStoreOpening
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * R7.2 review fixes, phone only (no C# twin): the store's start and migration decisions ([TaskModelStartup]) run against fakes of the
 * app's prefs, so every refusal is checked on the JVM: an unreadable old store is never migrated as empty (item 1), an unreadable task
 * list closes the store, is set aside once and never overwritten, and comes back merged at a later start (item 2), a backup that
 * isn't kept stops the migration (item 4), numbering waits for a PC (item 5), the notice for a PC without `tasks` (item 3), and the
 * NITs (Add to Today compares content, a fresh backup on every attempt, the store's commit and the old screens' edit path).
 */
object R72FixSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val PHONE2 = "cccccccc-0000-0000-0000-00000000000c"
    private val zone: ZoneId = ZoneId.of("America/New_York")
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-12T17:00:00Z")

    private fun id(n: Int) = "72f1f1f1-0000-0000-0000-%012d".format(n)

    private fun task(n: Int, bin: TaskBin, by: String = PC, number: TaskNumber? = TaskNumber(n)): SyncTaskItem {
        val created = OffsetDateTime.parse("2026-11-10T13:00:00Z").plusMinutes(n.toLong())
        return SyncTaskItem(TaskItem(id(n), "Task $n", bin, n.toDouble(), created, created, created, number = number), TaskFieldStamps.allAt(FieldStamp(created, by)))
    }

    private fun world() = TaskLists(listOf(task(1, TaskBin.LATER), task(2, TaskBin.NEXT), task(3, TaskBin.SORT)), emptyList(), emptyList(), emptyList())

    private fun parts(lists: TaskLists) = TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account)

    fun run(report: CheckReport) {
        oldStores(report)
        migration(report)
        opening(report)
        numbering(report)
        notice(report)
        addSent(report)
        storeEdits(report)
    }

    // ---- Item 1: an old store that is stored but won't open or read is never migrated as empty ----

    private fun oldStores(report: CheckReport) {
        val good = listOf(
            TaskOldStore("shared_lists/lists", "sealed", """{"nextTasks":[]}""", TaskOldStore.Shape.OBJECT),
            TaskOldStore("task_categories/list", null, null, TaskOldStore.Shape.ARRAY),
            TaskOldStore("task_filter_presets/list", "sealed", "[]", TaskOldStore.Shape.ARRAY),
        )
        report.check("r72-fix (1): stores that open and read, or hold nothing, can be migrated", TaskModelStartup.unreadableOldStores(good).isEmpty())
        val keyLost = good.mapIndexed { index, store -> if (index == 0) store.copy(opened = null) else store }
        report.check("r72-fix (1): a stored value its key can't open is unreadable", TaskModelStartup.unreadableOldStores(keyLost) == listOf("shared_lists/lists"))
        val damaged = good.mapIndexed { index, store -> if (index == 2) store.copy(opened = "[{\"name\":") else store }
        report.check("r72-fix (1): a value that opens but isn't JSON is unreadable", TaskModelStartup.unreadableOldStores(damaged) == listOf("task_filter_presets/list"))
        val wrongShape = good.mapIndexed { index, store -> if (index == 0) store.copy(opened = "[]") else store }
        report.check("r72-fix (1): JSON of the wrong shape is unreadable", TaskModelStartup.unreadableOldStores(wrongShape) == listOf("shared_lists/lists"))
    }

    /** The app's side of the migration, over a map standing in for the prefs; it records every write. */
    private class FakePort(var stores: List<TaskOldStore>, val backupKept: Boolean = true, val activateKept: Boolean = true) : TaskMigrationPort {
        val prefs = mutableMapOf<String, String>()
        val lines = mutableListOf<String>()
        var backups = 0
        var folderBackups = 0
        var switches = 0
        var marker = false

        override fun version() = "0.1.78"
        override fun oldStores() = stores
        override fun keepUnreadable(names: List<String>) {
            for (store in stores.filter { it.name in names }) prefs.putIfAbsent("unreadable-old-${store.name}", store.stored!!)
        }
        override fun folderBackedUp(version: String) = prefs["attempt"] == version
        override fun recordBackup(version: String): Boolean {
            backups++
            if (!backupKept) return false
            prefs["before-model-$version"] = stores.joinToString("|") { it.stored.orEmpty() }
            prefs["attempt"] = version
            return true
        }
        override fun folderBackup() {
            folderBackups++
        }

        // As the app reads its old stores: leniently, so an unreadable one reads as empty (the reason item 1 exists).
        override fun switchOutcome(): TaskModelSwitch.Outcome {
            switches++
            val input = TaskMigrationInput(emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), now, zone, isNumberingDevice = false)
            return TaskModelSwitch.decide(TaskMigration.run(input), PHONE, null, now, zone)
        }

        override fun activate(lists: TaskLists, viewSearches: Map<String, String>): Boolean {
            if (!activateKept) return false
            prefs["lists"] = TaskJson.writeParts(TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account)).toString()
            marker = true
            return true
        }

        override fun log(line: String) {
            lines += line
        }
    }

    private fun migration(report: CheckReport) {
        val readable = listOf(TaskOldStore("shared_lists/lists", "sealed-a", """{"nextTasks":[]}""", TaskOldStore.Shape.OBJECT))
        val unreadable = listOf(TaskOldStore("shared_lists/lists", "sealed-a", null, TaskOldStore.Shape.OBJECT))

        // Item 1 (mutation target): the store holds something its key can't open: nothing written, no marker, old store untouched.
        val lost = FakePort(unreadable)
        val switched = TaskModelStartup.migrate(lost)
        report.check(
            "r72-fix (1): an unreadable old store: no marker, no lists, no backup, the migration rule never runs, only its sealed value kept aside",
            !switched && !lost.marker && lost.prefs.keys == setOf("unreadable-old-shared_lists/lists") && lost.backups == 0 && lost.switches == 0 && lost.stores == unreadable,
            lost.lines.joinToString(" / "),
        )
        report.check("r72-fix (1): its sealed value is kept aside as it was", lost.prefs["unreadable-old-shared_lists/lists"] == "sealed-a")
        lost.stores = listOf(unreadable.single().copy(stored = "sealed-overwritten"))
        TaskModelStartup.migrate(lost)
        report.check("r72-fix (1): a later start never overwrites the copy kept aside (the old screens may have written over the store)", lost.prefs["unreadable-old-shared_lists/lists"] == "sealed-a" && !lost.marker)
        lost.lines.removeAt(1)
        report.check("r72-fix (1): the log names the store, never its text", lost.lines.size == 1 && lost.lines[0].contains("shared_lists/lists") && !lost.lines[0].contains("sealed-a"))
        lost.stores = readable
        report.check("r72-fix (1): the next start, the key back: it migrates", TaskModelStartup.migrate(lost) && lost.marker)

        // Item 4 (mutation target): a backup that isn't kept stops it before the migration rule.
        val noBackup = FakePort(readable, backupKept = false)
        report.check(
            "r72-fix (4): a backup commit that fails: no marker, the migration rule never runs, no folder backup",
            !TaskModelStartup.migrate(noBackup) && !noBackup.marker && noBackup.switches == 0 && noBackup.folderBackups == 0 && !noBackup.prefs.containsKey("lists"),
        )

        // NIT: every attempt takes a fresh copy of the old stores; the folder backup once per version.
        val retry = FakePort(readable, activateKept = false)
        TaskModelStartup.migrate(retry)
        retry.stores = listOf(readable.single().copy(stored = "sealed-b"))
        TaskModelStartup.migrate(retry)
        report.check(
            "r72-fix (NIT): a retry in the same version takes a fresh copy (the old stores changed), the folder backup only once",
            retry.backups == 2 && retry.folderBackups == 1 && retry.prefs["before-model-0.1.78"] == "sealed-b" && !retry.marker,
        )
        report.check("r72-fix: a commit of the lists that fails leaves no marker", retry.lines.any { it.contains("could not be written") } && !retry.marker)
    }

    // ---- Item 2: an unreadable task list closes the store; set aside once, never overwritten; merged back when it opens ----

    /** PhoneTaskStore.load over a map standing in for its prefs ("S:" + JSON = a sealed value; [keyLost]: nothing opens). */
    private class FakeStore {
        val prefs = mutableMapOf<String, String>()
        var keyLost = false
        var closed = false
        var lists: TaskLists? = null
        var lastLine = ""

        fun seal(lists: TaskLists) = "S:" + TaskJson.writeParts(TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account)).toString()

        private fun read(sealed: String): TaskParts? =
            if (keyLost || !sealed.startsWith("S:")) null else runCatching { TaskJson.readParts(Json.parseToJsonElement(sealed.drop(2)) as JsonObject) }.getOrNull()

        fun start() {
            val opening = TaskModelStartup.open(prefs["lists"], prefs["lists.unreadable"], ::read, PHONE, now)
            lastLine = opening.logLine
            when (opening) {
                is TaskStoreOpening.Closed -> {
                    if (opening.setAside) prefs["lists.unreadable"] = prefs["lists"]!!
                    closed = true
                    lists = null
                }
                is TaskStoreOpening.Open -> {
                    if (opening.write) {
                        prefs["lists"] = seal(TaskModelStartup.listsOf(opening.parts))
                        if (opening.clearSetAside) prefs.remove("lists.unreadable")
                    }
                    closed = false
                    lists = TaskModelStartup.listsOf(opening.parts)
                }
            }
        }
    }

    private fun opening(report: CheckReport) {
        val store = FakeStore()
        // What the phone held (a phone-only task 4 among them) when the key went missing.
        val held = TaskLists(world().tasks + task(4, TaskBin.LATER, by = PHONE), emptyList(), emptyList(), emptyList())
        store.prefs["lists"] = store.seal(held)
        store.start()
        report.check("r72-fix (2): the lists read back as activated (every part written, read the same)", !store.closed && store.lists == held)

        val first = store.prefs["lists"]!!
        store.keyLost = true
        store.start()
        report.check("r72-fix (2): an unreadable task list closes the store (nothing shown or kept from it) and is set aside as it was", store.closed && store.lists == null && store.prefs["lists.unreadable"] == first, store.lastLine)

        // A second failure with other bytes (an older build had started empty and refilled from sync meanwhile).
        val refilled = TaskLists(world().tasks, emptyList(), emptyList(), emptyList())
        store.prefs["lists"] = "X:damaged"
        store.start()
        report.check("r72-fix (2): a second failure never overwrites the copy already set aside", store.closed && store.prefs["lists.unreadable"] == first && store.prefs["lists"] == "X:damaged", store.lastLine)

        // The key is back: the main lists open and the set-aside copy is merged in (task 4 comes back), then dropped.
        store.prefs["lists"] = store.seal(refilled)
        store.keyLost = false
        store.start()
        val back = store.lists?.tasks.orEmpty()
        report.check(
            "r72-fix (2): at the next start that can read it, the set-aside copy is merged back (its phone-only task returns) and dropped",
            !store.closed && back.any { it.id == id(4) && it.task.isLive } && back.size == 4 && !store.prefs.containsKey("lists.unreadable"),
            store.lastLine,
        )
        val again = store.lists
        store.start()
        report.check("r72-fix (2): the start after that changes nothing", store.lists == again && store.lastLine == "task list read")

        // A set-aside copy that still can't be read is kept, and the lists are used.
        val partly = FakeStore()
        partly.prefs["lists"] = partly.seal(refilled)
        partly.prefs["lists.unreadable"] = "X:still damaged"
        partly.start()
        report.check("r72-fix (2): a set-aside copy that still can't be read is kept; the readable lists are used", !partly.closed && partly.prefs["lists.unreadable"] == "X:still damaged" && partly.lists == refilled)

        // The marker without lists (never written by activate): closed, nothing to set aside.
        val missing = FakeStore()
        missing.start()
        report.check("r72-fix (2): the marker without lists closes the store too (the old lists are not used)", missing.closed && missing.prefs.isEmpty())

        // The merge back is the PC's MergeLists: the same as merging the two copies as another device's.
        val mergedBoth = TaskModelStartup.open(store.seal(refilled), store.seal(held), { TaskJson.readParts(Json.parseToJsonElement(it.drop(2)) as JsonObject) }, PHONE, now)
        report.check(
            "r72-fix (2): the merge back is TaskListMerge.mergeLists of the two copies",
            mergedBoth is TaskStoreOpening.Open && TaskModelStartup.listsOf(mergedBoth.parts) == com.accountabilibuddy.shared.tasks.TaskListMerge.mergeLists(refilled, held, PHONE, now),
        )
    }

    // ---- Item 5: final numbers on the phone only when the group is known to have no PC ----

    private fun numbering(report: CheckReport) {
        val phone = TaskNumbering.PHONE_KIND
        val pc = TaskNumbering.PC_KIND
        report.check("r72-fix (5): an unjoined phone numbers nothing final", TaskNumbering.groupNumberingDevice(false, listOf(PHONE2 to phone), PHONE) == null)
        report.check("r72-fix (5): joined, no file received yet (a PC may be in the group): temporary numbers", TaskNumbering.groupNumberingDevice(true, emptyList(), PHONE) == null)
        report.check("r72-fix (5): a phone-only group (a phone's file, no PC's): the lowest phone numbers", TaskNumbering.groupNumberingDevice(true, listOf(PHONE2 to phone), PHONE) == PHONE)
        report.check("r72-fix (5): a PC's file received: the PC numbers, never the phone", TaskNumbering.groupNumberingDevice(true, listOf(PHONE2 to phone, PC to pc), PHONE) == PC)
        report.check("r72-fix (5): ids compare as lower-case text", TaskNumbering.groupNumberingDevice(true, listOf(PC.uppercase() to pc), PHONE) == PC)

        // Through the store's commit: a phone task added before any file arrived keeps a temporary number.
        val added = TaskLists(world().tasks + task(5, TaskBin.SORT, by = PHONE, number = null), emptyList(), emptyList(), emptyList())
        val waiting = TaskModelStartup.committed(world(), added, PHONE, TaskNumbering.groupNumberingDevice(true, emptyList(), PHONE), now, zone)
        report.check("r72-fix (5): before any file, a new task's number is temporary", waiting?.tasks?.first { it.id == id(5) }?.task?.number?.temporary == true)
        val alone = TaskModelStartup.committed(world(), added, PHONE, TaskNumbering.groupNumberingDevice(true, listOf(PHONE2 to phone), PHONE), now, zone)
        report.check("r72-fix (5): in a phone-only group it is final", alone?.tasks?.first { it.id == id(5) }?.task?.number?.temporary == false)
    }

    // ---- Item 3: the notice for a PC that writes no `tasks` ----

    private fun file(name: String, kind: String, withTasks: Boolean, oldLists: Boolean): SyncFile {
        val card = SyncDeviceCard(UUID.nameUUIDFromBytes(name.toByteArray()), name, kind, "0.1.130", Instant.parse("2026-11-12T16:00:00Z"), emptyList(), false, emptyMap())
        return SyncFile(
            1, card, emptyList(), emptyList(), null, emptyList(),
            nextTasks = if (oldLists) emptyList() else null,
            later = if (oldLists) emptyList() else null,
            taskParts = if (withTasks) TaskParts(emptyList(), emptyList(), emptyList(), emptyList(), null) else null,
        )
    }

    private fun notice(report: CheckReport) {
        val old = file("Desk", SyncDeviceCard.KIND_PC, withTasks = false, oldLists = true)
        val updated = file("Laptop", SyncDeviceCard.KIND_PC, withTasks = true, oldLists = true)
        val notSharing = file("Studio", SyncDeviceCard.KIND_PC, withTasks = false, oldLists = false)
        val olderPhone = file("Pixel", SyncDeviceCard.KIND_PHONE, withTasks = false, oldLists = true)
        val names = TaskListUpdateNotice.pcsWithoutTasks(listOf(old, updated, notSharing, olderPhone))
        report.check("r72-fix (3): only a PC sharing old lists without `tasks` is named (not an updated PC, one not sharing, or a phone)", names == listOf("Desk"), names.toString())
        report.check("r72-fix (3): the line names it", TaskListUpdateNotice.text(names) == "Update AccountabiliBuddy on Desk: its task edits don't reach this phone until it does.")
        report.check("r72-fix (3): no such PC: no line", TaskListUpdateNotice.text(TaskListUpdateNotice.pcsWithoutTasks(listOf(updated))) == null)
        report.check("r72-fix (3): two PCs", TaskListUpdateNotice.text(listOf("Desk", "Den"))!!.startsWith("Update AccountabiliBuddy on Desk and Den: their"))
    }

    // ---- NIT: Add to Today compares what the item says with what was sent ----

    private fun addSent(report: CheckReport) {
        val sent = """{"title":"Write intro","steps":[{"title":"Outline"},{"title":"Draft"}],"estimateMinutes":25,"note":"keep it short"}"""
        report.check("r72-fix (NIT): unchanged content is not a change (whatever its stamp did)", TaskAddSent.changed(sent, "Write intro ", "keep it short", listOf("Outline", "Draft"), 25) == false)
        report.check("r72-fix (NIT): a new title is a change", TaskAddSent.changed(sent, "Write the intro", "keep it short", listOf("Outline", "Draft"), 25) == true)
        report.check("r72-fix (NIT): a new note is a change", TaskAddSent.changed(sent, "Write intro", null, listOf("Outline", "Draft"), 25) == true)
        report.check("r72-fix (NIT): a step added is a change", TaskAddSent.changed(sent, "Write intro", "keep it short", listOf("Outline", "Draft", "Edit"), 25) == true)
        report.check("r72-fix (NIT): an unreadable request falls back to the stamp", TaskAddSent.changed("not json", "x", null, emptyList(), null) == null && TaskAddSent.changed(null, "x", null, emptyList(), null) == null)
    }

    // ---- The store's commit and the old screens' edit path (SharedListsStore.edit -> PhoneTaskStore.change) ----

    private fun storeEdits(report: CheckReport) {
        val world = world()
        report.check("r72-fix: a change that changes nothing keeps nothing (no write)", TaskModelStartup.committed(world, world, PHONE, PC, now, zone) == null)

        // SharedListsStore.edit: the old screens' views before and after a change -> only the changed items -> TaskLocalEdits -> commit.
        val before = OldTaskFields(TaskFacadeLists.draft(world, now), TaskFacadeLists.later(world, now), TaskFacadeLists.parked(world, now), emptyList(), emptyList(), TaskFacadeLists.autoSaved(world, now))
        val item = before.later.first { it.id == id(1) }
        val after = before.copy(later = before.later.map { if (it.id == id(1)) it.copy(title = "Renamed here") else it } + item.copy(id = id(9), title = "Added here", position = 9.0))
        val changed = TaskModelStartup.facadeChanges(before, after)
        report.check("r72-fix: the edit hands over only what it changed (by id)", changed.later.map { it.id } == listOf(id(1), id(9)) && changed.nextTasks.isEmpty() && changed.parked.isEmpty())
        val kept = TaskModelStartup.committed(world, TaskLocalEdits.apply(world, changed, PHONE, now, zone), PHONE, PC, now, zone)
        val renamed = kept?.tasks?.first { it.id == id(1) }
        val made = kept?.tasks?.first { it.id == id(9) }
        report.check(
            "r72-fix: through the store, the rename is the phone's stamped title edit (number kept) and the new task gets a temporary number (the PC numbers)",
            renamed?.task?.title == "Renamed here" && renamed.stamps.of(TaskField.TITLE)?.by == PHONE && renamed.task.number == TaskNumber(1) &&
                made?.task?.bin == TaskBin.LATER && made.task.number?.temporary == true,
        )
        report.check("r72-fix: the same views again change nothing", TaskModelStartup.facadeChanges(after, after).let { it.later.isEmpty() && it.nextTasks.isEmpty() })
    }
}
