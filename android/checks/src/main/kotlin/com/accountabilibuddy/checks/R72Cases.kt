package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncFileStrip
import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskHistoryRules
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskMigration
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPreset
import com.accountabilibuddy.shared.tasks.TaskModelSwitch
import com.accountabilibuddy.shared.tasks.TaskParts
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.ZoneId

/**
 * R7.2: the phone's task store rules against the PC's (area `r72`, written by the C# checks' `R72Fixtures`): Normalize, History,
 * the store's migration, the old fields while a PC session runs (Q128), a file without `tasks` (Q583), what a kept copy and a QR file
 * leave out, and the parts of the phone's own file.
 */
object R72Cases {
    private fun lists(json: JsonElement): TaskLists {
        val obj = json.jsonObject
        return TaskLists(
            R71Json.tasks(obj.getValue("tasks")), R71Json.labels(obj.getValue("labels")), R71Json.views(obj.getValue("views")), R71Json.log(obj.getValue("log")),
            obj["account"]?.takeIf { it !is JsonNull }?.let(TaskJson::readAccountSettingsObject),
        )
    }

    private fun text(expected: JsonObject, key: String): String? = expected[key]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content

    /** Null when [actual] is, part by part, the PC's text of [expected]'s lists. */
    private fun sameLists(expected: JsonObject, actual: TaskLists): String? {
        GoalCaseRunner.sameText(text(expected, "tasks")!!, TaskJson.text(TaskJson.writeTasks(actual.tasks)))?.let { return "tasks: $it" }
        GoalCaseRunner.sameText(text(expected, "labels")!!, TaskJson.text(TaskJson.writeLabels(actual.labels)))?.let { return "labels: $it" }
        GoalCaseRunner.sameText(text(expected, "views")!!, TaskJson.text(TaskJson.writeViews(actual.views)))?.let { return "views: $it" }
        GoalCaseRunner.sameText(text(expected, "log")!!, TaskJson.text(TaskJson.writeTaskLog(actual.log)))?.let { return "log: $it" }
        val account = actual.account?.let { TaskJson.text(TaskJson.writeAccountSettings(it)) }
        if (text(expected, "account") != account) return "account: C# ${text(expected, "account")} vs kotlin $account"
        return null
    }

    private fun zone(case: FixtureCase): ZoneId? = R71Json.textOrNull(case, "zone")?.let { ZoneId.of(it) }

    fun normalize(case: FixtureCase): String? {
        val result = TaskListRules.normalize(
            lists(case.input.getValue("lists")), case.text("self"), R71Json.textOrNull(case, "numberingDevice"), R71Json.time(case), zone(case),
        )
        return sameLists(case.expected.jsonObject, result)
    }

    fun history(case: FixtureCase): String? {
        val held = lists(case.input.getValue("lists"))
        val now = R71Json.time(case)
        val self = case.text("self")
        val result = when (case.text("op")) {
            "remove" -> {
                val kind = TaskHistoryKind.entries.first { it.wire == case.text("kind") }
                TaskHistoryRules.remove(held, R71Json.texts(case.input["ids"])!!, kind, now, self)
            }

            "bringBack" -> TaskHistoryRules.bringBack(held, case.text("entryId"), now.plusMinutes(case.input.getValue("shift").jsonPrimitive.int.toLong()), self)
            else -> TaskHistoryRules.Result(TaskHistoryRules.reconciled(held, now), emptyList())
        }
        val expected = case.expected.jsonObject
        val ids = R71Json.texts(expected["ids"])!!
        if (ids != result.ids.map { it.lowercase() }) return "ids: C# $ids vs kotlin ${result.ids}"
        return sameLists(expected, result.lists)
    }

    fun storeMigration(case: FixtureCase): String? {
        val input = case.input
        val self = case.text("self")
        val numbering = R71Json.textOrNull(case, "numberingDevice")
        val migration = TaskMigrationInput(
            SyncFileJson.readCategories(input["categories"])!!,
            SyncFileJson.readNextTasks(input["nextTasks"])!!,
            SyncFileJson.readLater(input["later"])!!,
            SyncFileJson.readParked(input["parked"])!!,
            SyncFileJson.readAutoSaved(input["autoSaved"])!!,
            input.getValue("presets").jsonArray.map { it.jsonObject }.map { preset ->
                TaskMigrationPreset(
                    preset.getValue("id").jsonPrimitive.content, preset.getValue("name").jsonPrimitive.content,
                    preset["category"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content, preset.getValue("search").jsonPrimitive.content,
                )
            },
            R71Json.time(case), ZoneId.of(case.text("zone")), isNumberingDevice = numbering != null && numbering.equals(self, ignoreCase = true),
        )
        val expected = case.expected.jsonObject
        val counts = TaskMigration.run(migration).counts
        if (!counts.isBalanced) return "the phone's counts don't balance"
        if (counts.sources.values.sum() != expected.getValue("read").jsonPrimitive.int) return "read: C# ${expected["read"]} vs kotlin ${counts.sources.values.sum()}"
        val bins = expected.getValue("bins").jsonObject.mapValues { it.value.jsonPrimitive.int }
        if (bins != counts.bins.entries.associate { it.key.wire to it.value }) return "bins: C# $bins vs kotlin ${counts.bins}"
        if (counts.history != expected.getValue("history").jsonPrimitive.int || counts.expired != expected.getValue("expired").jsonPrimitive.int ||
            counts.duplicates != expected.getValue("duplicates").jsonPrimitive.int
        ) {
            return "history / expired / duplicates differ: $counts"
        }

        val outcome = TaskModelSwitch.run(migration, self, numbering)
        val kept = outcome.lists ?: return "the store refused a balanced migration"
        val searches = expected.getValue("searches").jsonObject.mapValues { it.value.jsonPrimitive.content }
        if (searches != outcome.viewSearches) return "searches: C# $searches vs kotlin ${outcome.viewSearches}"
        return sameLists(expected, kept)
    }

    fun oldFieldsSession(case: FixtureCase): String? {
        val held = lists(case.input.getValue("held"))
        val incoming = lists(case.input.getValue("incoming"))
        val now = R71Json.time(case)
        val zone = zone(case)!!
        val self = case.text("self")
        val parts = TaskParts(incoming.tasks, incoming.labels, incoming.views, incoming.log, incoming.account)
        val merged = TaskListRules.normalize(TaskListRules.mergeFile(held, parts, self, now)!!, self, case.text("numberingDevice"), now, zone)
        val (old, _) = TaskListRules.fileParts(merged, now, zone)
        return OldFieldJson.difference(case.expected, R71OldFieldCases.json(old))
    }

    fun receiveWithoutTasks(case: FixtureCase): String? {
        val held = lists(case.input.getValue("held"))
        val file = SyncFileJson.read(Json.parseToJsonElement(case.text("file")).jsonObject) ?: return "the phone can't read the file"
        val result = TaskListRules.mergeFile(held, file.taskParts, case.text("self"), R71Json.time(case)) ?: held
        return sameLists(case.expected.jsonObject, result)
    }

    fun strip(case: FixtureCase): String? {
        val file = SyncFileJson.read(Json.parseToJsonElement(case.text("file")).jsonObject) ?: return "the phone can't read the file"
        val wanted = R71Json.texts(case.input["parts"])!!
        val parts = file.taskParts ?: return "the phone read no task parts"
        if (listOf(parts.tasks, parts.labels, parts.views, parts.taskLog, parts.accountSettings).any { it == null }) return "the phone missed a part: $parts"
        fun present(written: com.accountabilibuddy.shared.sync.SyncFile) = SyncFileJson.write(written).keys.filter { it in wanted }
        val expected = case.expected.jsonObject
        val original = present(file)
        if (original != R71Json.texts(expected["original"])) return "written again: C# ${expected["original"]} vs kotlin $original"
        val kept = present(SyncFileStrip.keptCopy(file, emptyList(), null))
        if (kept != R71Json.texts(expected["kept"])) return "kept copy: C# ${expected["kept"]} vs kotlin $kept"
        val qr = present(SyncFileStrip.forQr(file))
        if (qr != R71Json.texts(expected["qr"])) return "QR file: C# ${expected["qr"]} vs kotlin $qr"
        return null
    }

    fun withParts(case: FixtureCase): String? {
        val (old, parts) = TaskListRules.fileParts(lists(case.input.getValue("lists")), R71Json.time(case), zone(case)!!)
        val expected = case.expected.jsonObject
        val oldExpected = JsonObject(expected.filterKeys { it in listOf("nextTasks", "later", "parked", "comebacks", "categories", "autoSaved") })
        OldFieldJson.difference(oldExpected, R71OldFieldCases.json(old))?.let { return "old fields: $it" }
        GoalCaseRunner.sameText(text(expected, "tasks")!!, TaskJson.text(TaskJson.writeTasks(parts.tasks!!)))?.let { return "tasks: $it" }
        GoalCaseRunner.sameText(text(expected, "labels")!!, TaskJson.text(TaskJson.writeLabels(parts.labels!!)))?.let { return "labels: $it" }
        GoalCaseRunner.sameText(text(expected, "views")!!, TaskJson.text(TaskJson.writeViews(parts.views!!)))?.let { return "views: $it" }
        GoalCaseRunner.sameText(text(expected, "taskLog")!!, TaskJson.text(TaskJson.writeTaskLog(parts.taskLog!!)))?.let { return "taskLog: $it" }
        val account = parts.accountSettings?.let { TaskJson.text(TaskJson.writeAccountSettings(it)) }
        if (text(expected, "account") != account) return "account: C# ${text(expected, "account")} vs kotlin $account"
        return null
    }
}
