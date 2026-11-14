package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncFileStrip
import com.accountabilibuddy.shared.tasks.TaskFacadeLists
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTagScope
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TagScopeEdits
import com.accountabilibuddy.shared.tasks.TagScopeMerge
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskModelStartup
import com.accountabilibuddy.shared.tasks.TaskParts
import com.accountabilibuddy.shared.tasks.TaskScopes
import com.accountabilibuddy.shared.tasks.TaskSearch
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.UUID

/**
 * LB1a (phone only parts; the merges, edits, visibility, hidden-keeping, clean-up and search tables are the shared lb1 fixtures, ADR 89):
 * an older file without the part leaves the scopes alone, a file with it (alone too) merges it, a tombstone makes the tag shared, the
 * part is written last and never goes in a QR file or the kept copy, a store's document keeps the scopes through its write and read, and
 * an unrelated normalize never rewrites a label's device list.
 */
object Lb1aSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val OTHER = "cccccccc-0000-0000-0000-00000000000c"
    private val noon: OffsetDateTime = OffsetDateTime.parse("2026-11-13T12:00:00+00:00")

    private fun at(minutes: Int): OffsetDateTime = noon.plusMinutes(minutes.toLong())

    private fun text(ids: List<String>?): String = if (ids == null) "shared" else ids.joinToString(",") { it.take(1) }

    fun run(report: CheckReport) {
        val work = TaskLabel(TaskLabel.idFor("Work"), "Work", "#2F6BEA", 1.0, at(-100), onlyOn = listOf(PC), onlyOnStamp = FieldStamp(at(-100), PC))
        val scope = SyncTagScope(TaskScopes.tagIdFor("Work"), "Work", listOf(PC), at(-100))
        val lists = TaskLists(emptyList(), listOf(SyncTaskLabel(work)), emptyList(), emptyList(), null, listOf(scope))

        // A file without the part leaves the scopes alone; one with it merges item by item, alone too.
        val older = TaskParts(emptyList(), emptyList(), emptyList(), emptyList(), null)
        val afterOlder = TaskListRules.mergeFile(lists, older, PHONE, at(0))
        report.check("lb1a: an older file without the part leaves the scopes and the label's list alone", afterOlder != null && afterOlder.tagScopes == listOf(scope) && text(afterOlder.labels.single().label.onlyOn) == "a")
        report.check("lb1a: no parts at all changes nothing", TaskListRules.mergeFile(lists, null, PHONE, at(0)) == null)
        val gym = SyncTagScope(TaskScopes.tagIdFor("gym"), "gym", listOf(PHONE), at(-10))
        val withScopes = TaskListRules.mergeFile(lists, older.copy(tagScopes = listOf(gym)), PHONE, at(0))
        report.check("lb1a: a file with the part merges it item by item", withScopes?.tagScopes?.size == 2 && withScopes.tagScopes!!.any { it.name == "gym" })
        val onlyScopes = TaskListRules.mergeFile(lists, TaskParts(null, null, null, null, null, tagScopes = listOf(gym)), PHONE, at(0))
        report.check("lb1a: a file whose only task part is the scopes still merges them (and leaves the tasks)", onlyScopes?.tagScopes?.size == 2 && onlyScopes.tasks.isEmpty())
        val tombstoned = TaskListRules.mergeFile(lists, older.copy(tagScopes = listOf(scope.deleted(at(-5)))), PHONE, at(0))
        report.check("lb1a: a tombstone from another device makes the tag shared here", tombstoned?.tagScopes?.single()?.deleted != null && TaskScopes.tagVisible("work", tombstoned.tagScopes, PHONE, null))
        report.check("lb1a: pruned keeps a live scope and drops a tombstone past 30 days",
            TaskListRules.pruned(lists, at(0), PC).tagScopes?.size == 1 && TaskListRules.pruned(lists.copy(tagScopes = listOf(scope.deleted(at(-31 * 24 * 60)))), at(0), PC).tagScopes == null)
        val normalized = TaskListRules.normalize(lists.copy(labels = listOf(SyncTaskLabel(work.copy(onlyOn = listOf(PC, "dddddddd-0000-0000-0000-00000000000d"))))), PC, PC, at(0), ZoneId.of("UTC"))
        report.check("lb1a: normalizing never rewrites a label's list or the scopes (a removed or unseen device writes nothing)",
            text(normalized.labels.single().label.onlyOn) == "a,d" && normalized.tagScopes == listOf(scope))

        // What this phone writes.
        val (_, parts) = TaskListRules.fileParts(lists, at(0), ZoneId.of("UTC"))
        report.check("lb1a: the phone's file parts carry the scopes; none to send leaves the part out",
            parts.tagScopes == listOf(scope) && TaskListRules.fileParts(lists.copy(tagScopes = null), at(0), ZoneId.of("UTC")).second.tagScopes == null && TaskListRules.fileParts(lists.copy(tagScopes = emptyList()), at(0), ZoneId.of("UTC")).second.tagScopes == null)
        val card = SyncDeviceCard(UUID.fromString(PHONE), "Phone", "phone", "0.1.78", Instant.parse("2026-11-13T12:00:00Z"), listOf("drive"), false, emptyMap())
        val file = SyncFile(1, card, emptyList(), emptyList(), null, emptyList(), taskParts = parts)
        val json = SyncFileJson.write(file)
        report.check("lb1a: the part is `tagScopes`, the last one written (after the other task parts and the notes)", json.keys.last() == "tagScopes" && json.getValue("tagScopes").jsonArray.size == 1, json.keys.toString())
        val scopeWire = json.getValue("tagScopes").jsonArray[0].jsonObject
        report.check("lb1a: a scope has id, name, onlyOn, editedAt in this order", scopeWire.keys.toList() == listOf("id", "name", "onlyOn", "editedAt"), scopeWire.keys.toString())
        val labelWire = json.getValue("labels").jsonArray[0].jsonObject.getValue("label").jsonObject
        report.check("lb1a: a label's onlyOn and onlyOnStamp are written after its other fields", labelWire.keys.toList() == listOf("id", "name", "color", "position", "editedAt", "onlyOn", "onlyOnStamp"), labelWire.keys.toString())
        val back = SyncFileJson.read(json)
        report.check("lb1a: the file reads back with the scopes and the label's list", back?.taskParts?.tagScopes == listOf(scope) && text(back.taskParts!!.labels?.single()?.label?.onlyOn) == "a")
        report.check("lb1a: a file with only the part carries task parts (the receipt counts it with tasks)", SyncFileJson.readTaskParts(JsonObject(mapOf("tagScopes" to json.getValue("tagScopes"))))?.tagScopes == listOf(scope))
        report.check("lb1a: a file without the part reads it as null, not an empty list", SyncFileJson.read(SyncFileJson.write(file.copy(taskParts = parts.copy(tagScopes = null))))?.taskParts?.tagScopes == null)
        val qr = SyncFileStrip.forQr(file)
        report.check("lb1a: a QR file carries no scopes", qr.taskParts == null && !SyncFileJson.write(qr).containsKey("tagScopes"))
        val kept = SyncFileStrip.keptCopy(file, null, null)
        report.check("lb1a: the copy kept of a received file carries no scopes", kept.taskParts == null && !SyncFileJson.write(kept).containsKey("tagScopes"))
        val keptWithMark = SyncFileStrip.keptCopy(file, null, null, tasksMark = true)
        report.check("lb1a: not even with the tasks mark", keptWithMark.taskParts?.tagScopes == null)

        // The store's document keeps the scopes through its write and read.
        val stored = TaskJson.writeParts(TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account, tagScopes = lists.tagScopes))
        val read = TaskJson.readParts(stored)
        report.check("lb1a: a store document keeps the scopes and the label's list (and none reads back as none)",
            TaskModelStartup.listsOf(read).tagScopes == listOf(scope) && text(read.labels?.single()?.label?.onlyOn) == "a"
                && TaskModelStartup.listsOf(TaskJson.readParts(TaskJson.writeParts(TaskParts(emptyList(), emptyList(), emptyList(), emptyList(), null, tagScopes = emptyList())))).tagScopes == null)
        val named = JsonObject(stored + mapOf("tagScopes" to JsonArray(emptyList())))
        report.check("lb1a: an empty part in a store document is none", TaskModelStartup.listsOf(TaskJson.readParts(named)).tagScopes == null)

        // The edits (the pickers will call these).
        val made = TaskLabelEdits.addWithId(TaskLists.EMPTY, "Errands", null, at(0), PHONE, onlyOnThisDevice = true)
        report.check("lb1a: a device-only label made here lists this phone, stamped by it", text(made.first.labels.single().label.onlyOn) == "b" && made.first.labels.single().label.onlyOnStamp?.by == PHONE)
        val scoped = TagScopeEdits.setScope(null, "gym", true, at(0), PHONE)
        report.check("lb1a: a device-only tag made here lists this phone", scoped?.single()?.name == "gym" && text(scoped.single().onlyOn) == "b")
        val tasks = listOf(
            com.accountabilibuddy.shared.tasks.TaskItem(
                id = "11111111-0000-0000-0000-000000000001", title = "Call the bank", bin = com.accountabilibuddy.shared.tasks.TaskBin.NEXT, position = 1.0, createdAt = at(-1000), editedAt = at(-1000),
                touchedAt = at(-1000), tags = listOf("private"),
            ),
        )
        val privateScope = listOf(SyncTagScope(TaskScopes.tagIdFor("private"), "private", listOf(PC), at(-10)))
        report.check("lb1a: search finds nothing hidden and everything without the tests",
            TaskSearch.matchingIds(tasks, "#private", emptyList(), null, TaskScopes.tagTest(privateScope, PHONE, null))!!.isEmpty()
                && TaskSearch.matchingIds(tasks, "#private", emptyList())!!.size == 1
                && TaskSearch.matchingIds(tasks, "#private", emptyList(), null, TaskScopes.tagTest(privateScope, PC, null))!!.size == 1)
        report.check("lb1a: older apps' labels (no list) are shared", TaskScopes.isVisible(null, PHONE, null) && TaskScopes.isVisible(listOf(PC), FieldStamp.NOBODY, null))
        report.check("lb1a: every writer keeps the hidden tags (the same helper the PC has)", TaskScopes.withHiddenTags(listOf("home"), listOf("home", "private"), privateScope, PHONE, null) == listOf("home", "private") && TaskScopes.withHiddenTags(emptyList(), listOf("private"), privateScope, PC, null) == null)
        report.check("lb1a: a label chosen here replaces a hidden one; a task shown as No label keeps it", TaskScopes.withHiddenLabel(null, work.id, listOf(work), PHONE, null) == work.id && TaskScopes.withHiddenLabel("x", work.id, listOf(work), PHONE, null) == "x")

        // The cap (the PC's TagScopeMerge.MaxScopes): the oldest tombstones go first, then the oldest live scopes.
        val many = (0 until TagScopeMerge.MAX_SCOPES + 30).map { index ->
            if (index < 20) SyncTagScope(TaskScopes.tagIdFor("tombstone $index"), "tombstone $index", listOf(PC), at(-5000 + index)).deleted(at(-4000 + index))
            else SyncTagScope(TaskScopes.tagIdFor("live $index"), "live $index", listOf(PC), at(-3000 + index))
        }
        val capped = TagScopeMerge.merge(many, emptyList(), at(0))
        report.check("lb1a: at most 2000 scopes; the oldest tombstones go first, then the oldest live ones",
            capped != null && capped.size == TagScopeMerge.MAX_SCOPES && capped.all { it.deleted == null } && capped.none { it.name == "live 20" || it.name == "live 29" } && capped.any { it.name == "live 30" })
        report.check("lb1a: under the limit nothing goes", TagScopeMerge.merge(many.take(TagScopeMerge.MAX_SCOPES), emptyList(), at(0))?.size == TagScopeMerge.MAX_SCOPES)

        // A rename still sees a hidden label; a join stamps the list only (the label's edit time stays), asked shared joins all the same.
        val hiddenWork = TaskLists(emptyList(), listOf(SyncTaskLabel(work.copy(onlyOn = listOf(PC), onlyOnStamp = FieldStamp(at(-100), PC)))), emptyList(), emptyList())
        val joinedAll = TaskLabelEdits.addWithId(hiddenWork, "Work", null, at(0), PHONE).first.labels.single().label
        report.check("lb1a: asking for a hidden name (not device-only) joins it and leaves the label's edit time", text(joinedAll.onlyOn) == "a,b" && joinedAll.editedAt == work.editedAt)
        // Name lookups by name (the old category screens' recolour, rename, delete) join a hidden label instead of binding to it unseen.
        val recoloured = TaskLabelEdits.recolour(hiddenWork, "Work", "#112233", at(0), PHONE).labels.single().label
        report.check("lb1a: recolour of a name that is a hidden label's joins it (this phone added), then recolours", text(recoloured.onlyOn) == "a,b" && recoloured.color == "#112233")
        val renamed = TaskLabelEdits.rename(hiddenWork, "Work", "Office", at(0), PHONE).labels.single().label
        report.check("lb1a: rename of a hidden label's name joins it first", text(renamed.onlyOn) == "a,b" && renamed.name == "Office")
        val twoLabels = TaskLists(emptyList(), hiddenWork.labels + SyncTaskLabel(TaskLabel(TaskLabel.idFor("Home"), "Home", "#E0322C", 2.0, at(-600))), emptyList(), emptyList())
        report.check("lb1a: a rename to a hidden label's name is refused (no two live labels share a name)", TaskLabelEdits.rename(twoLabels, "Home", "Work", at(0), PHONE) == twoLabels)
        report.check("lb1a: delete of a hidden label's name does nothing (a device can't delete what it can't see)", TaskLabelEdits.delete(hiddenWork, "Work", at(0), PHONE) == hiddenWork)

        // Every phone writer of a task's tags or label keeps what this phone can't see (the PC's per-writer checks): the Shelf edits, the old lists.
        val secret = TaskLabel(TaskLabel.idFor("Secret"), "Secret", "#E0322C", 2.0, at(-100), onlyOn = listOf(PC), onlyOnStamp = FieldStamp(at(-100), PC))
        val hideScope = listOf(SyncTagScope(TaskScopes.tagIdFor("private"), "private", listOf(PC), at(-10)))
        val taskId = "11111111-0000-0000-0000-000000000001"
        fun world(vararg tags: String, label: String? = secret.id): TaskLists = TaskLists(
            listOf(
                SyncTaskItem(
                    com.accountabilibuddy.shared.tasks.TaskItem(
                        id = taskId, title = "Call the bank", bin = com.accountabilibuddy.shared.tasks.TaskBin.QUEUED, position = 1.0, createdAt = at(-1000), editedAt = at(-1000),
                        touchedAt = at(-1000), labelId = label, tags = tags.toList().ifEmpty { null },
                    ),
                    com.accountabilibuddy.shared.tasks.TaskFieldStamps.NONE,
                ),
            ),
            listOf(SyncTaskLabel(work.copy(onlyOn = null, onlyOnStamp = null)), SyncTaskLabel(secret)), emptyList(), emptyList(), null, hideScope,
        )
        val held = world("private", "home")
        val noLabel = com.accountabilibuddy.shared.tasks.ShelfEdits.editAll(held, listOf(taskId), com.accountabilibuddy.shared.tasks.TaskField.LABEL, PHONE, at(0)) { it.copy(labelId = null) }.lists
        report.check("lb1a/writer: the Shelf's label edit (select mode, ⋮ sheet) keeps the hidden label", noLabel.tasks.single().task.labelId == secret.id)
        val noTags = com.accountabilibuddy.shared.tasks.ShelfEdits.edit(held, taskId, com.accountabilibuddy.shared.tasks.TaskField.TAGS, PHONE, at(0)) { it.copy(tags = null) }.lists
        report.check("lb1a/writer: the Shelf's tag edit keeps the hidden tag", noTags.tasks.single().task.tags == listOf("private"))
        val typedTag = com.accountabilibuddy.shared.tasks.ShelfEdits.editAll(world(), listOf(taskId), com.accountabilibuddy.shared.tasks.TaskField.TAGS, PHONE, at(0)) { it.copy(tags = listOf("Private")) }.lists
        report.check("lb1a/writer: a hidden tag typed in the Shelf joins its scope (this phone added), never made shared", text(typedTag.tagScopes!!.single().onlyOn) == "a,b")
        val added = com.accountabilibuddy.shared.tasks.ShelfEdits.add(
            world(label = null), "22222222-0000-0000-0000-000000000002", "New", com.accountabilibuddy.shared.tasks.TaskBin.QUEUED, secret.id, listOf("private"), PHONE, at(0),
        ).lists
        report.check("lb1a/writer: the Add sheet's hidden label and tag typed are joined, not made shared", text(added.labels.map { it.label }.single { it.id == secret.id }.onlyOn) == "a,b" && text(added.tagScopes!!.single().onlyOn) == "a,b")
        val stripped = TaskFacadeLists.draft(held, at(0)).single().copy(category = null, categoryColor = null, labels = null)
        val local = com.accountabilibuddy.shared.tasks.TaskLocalEdits.apply(held, com.accountabilibuddy.shared.tasks.OldTaskFields(listOf(stripped.copy(editedAt = at(5).toInstant())), emptyList(), emptyList(), emptyList(), emptyList(), emptyList()), PHONE, at(5), ZoneId.of("UTC"))
        report.check("lb1a/writer: the old lists' edit (which show only what this phone shows) keeps the hidden label and tag", local.tasks.single().task.labelId == secret.id && local.tasks.single().task.tags == listOf("private"))
    }
}
