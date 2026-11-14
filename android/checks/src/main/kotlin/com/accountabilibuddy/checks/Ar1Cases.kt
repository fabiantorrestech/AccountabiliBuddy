package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncKeptPast
import com.accountabilibuddy.shared.tasks.ArchiveBuilder
import com.accountabilibuddy.shared.tasks.ArchiveInput
import com.accountabilibuddy.shared.tasks.ArchiveReceivedDevice
import com.accountabilibuddy.shared.tasks.ArchiveRows
import com.accountabilibuddy.shared.tasks.TaskLists
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Instant
import java.time.OffsetDateTime

/**
 * AR1-fix (area `ar1`, written by the C# checks' `Ar1Fixtures`): the phone's ArchiveBuilder against the PC's Core one, over the same
 * made-up worlds. Every row (id, title, times, source, PC, category, steps, sort, what it offers) and every Recently deleted line
 * must be the PC's.
 */
object Ar1Cases {
    private fun lists(json: JsonElement): TaskLists {
        val obj = json.jsonObject
        return TaskLists(R71Json.tasks(obj.getValue("tasks")), R71Json.labels(obj.getValue("labels")), R71Json.views(obj.getValue("views")), R71Json.log(obj.getValue("log")))
    }

    private fun instant(text: String): Instant = OffsetDateTime.parse(text).toInstant()

    private fun text(json: JsonObject, key: String): String = json.getValue(key).jsonPrimitive.content

    // A kept task is carried as the PC wrote it ({sourceDeviceId, sourceName, sessionStart, task, editedAt}); its task reads like a history task.
    private fun kept(json: JsonElement): List<SyncKeptPast> = json.jsonArray.mapNotNull { element ->
        val item = element.jsonObject
        val task = SyncFileJson.readTaskHistory(JsonArray(listOf(JsonObject(mapOf("tasks" to JsonArray(listOf(item.getValue("task")))))))).orEmpty()
            .firstOrNull()?.tasks?.firstOrNull() ?: return@mapNotNull null
        SyncKeptPast(
            text(item, "sourceDeviceId"), item["sourceName"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content ?: "",
            item["sessionStart"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content?.let(::instant), task, instant(text(item, "editedAt")),
        )
    }

    private fun input(case: FixtureCase): ArchiveInput {
        val json = case.input
        return ArchiveInput(
            SyncFileJson.readTaskHistory(json.getValue("own")).orEmpty(),
            json.getValue("received").jsonArray.map { element ->
                val device = element.jsonObject
                ArchiveReceivedDevice(text(device, "deviceId"), text(device, "name"), SyncFileJson.readTaskHistory(device.getValue("entries")).orEmpty())
            },
            kept(json.getValue("kept")),
            SyncFileJson.readPastSettled(json.getValue("settled")).orEmpty(),
            lists(json.getValue("lists")),
            instant(case.text("now")),
            case.input["running"]?.jsonArray?.map { it.jsonPrimitive.content }?.toSet().orEmpty(),
        )
    }

    private fun time(value: Instant?): String = value?.toString() ?: ""

    // The PC's row, as a line of the facts that must agree (times as instants).
    private fun pcLines(expected: JsonObject): List<String> {
        val tasks = expected.getValue("tasks").jsonArray.map { element ->
            val row = element.jsonObject
            fun at(key: String) = text(row, key).takeIf { it.isNotEmpty() }?.let(::instant)?.let(::time).orEmpty()
            val steps = row.getValue("steps").jsonArray.joinToString(";") { step -> "${text(step.jsonObject, "id")}/${text(step.jsonObject, "title")}/${text(step.jsonObject, "doneAt").takeIf { it.isNotEmpty() }?.let(::instant)?.let(::time).orEmpty()}" }
            "task ${text(row, "id")}|${text(row, "title")}|${at("doneAt")}|${at("sessionStart")}|${(row.getValue("outside") as JsonPrimitive).booleanOrNull}|${text(row, "source")}|" +
                "${text(row, "deviceId")}|${text(row, "deviceName")}|${text(row, "categoryId")}|${text(row, "category")}|${text(row, "categoryColor")}|[$steps]|${at("sort")}|" +
                "${row.getValue("canTick").jsonPrimitive.content}|${row.getValue("canDelete").jsonPrimitive.content}|${row.getValue("canEdit").jsonPrimitive.content}"
        }
        val deleted = expected.getValue("deleted").jsonArray.map { element ->
            val row = element.jsonObject
            "deleted ${text(row, "id")}|${text(row, "title")}|${time(instant(text(row, "deletedAt")))}|${text(row, "source")}|${text(row, "deviceName")}|${text(row, "entryId")}|${row.getValue("canRestore").jsonPrimitive.content}"
        }
        return tasks + deleted
    }

    private fun phoneLines(rows: ArchiveRows): List<String> {
        fun source(value: com.accountabilibuddy.shared.tasks.ArchiveSource) = when (value) {
            com.accountabilibuddy.shared.tasks.ArchiveSource.OWN -> "Own"
            com.accountabilibuddy.shared.tasks.ArchiveSource.KEPT -> "Kept"
            com.accountabilibuddy.shared.tasks.ArchiveSource.RECEIVED -> "Received"
            com.accountabilibuddy.shared.tasks.ArchiveSource.TASK_LIST -> "TaskList"
        }
        val tasks = rows.tasks.map { row ->
            val steps = row.steps.joinToString(";") { step -> "${step.id}/${step.title}/${time(step.doneAt)}" }
            "task ${row.id}|${row.title}|${time(row.doneAt)}|${time(row.sessionStart)}|${row.outside}|${source(row.source)}|${row.deviceId.orEmpty()}|${row.deviceName.orEmpty()}|" +
                "${row.categoryId.orEmpty()}|${row.category.orEmpty()}|${row.categoryColor.orEmpty()}|[$steps]|${time(row.sort)}|${row.canTick}|${row.canDelete}|${row.canEdit}"
        }
        val deleted = rows.deleted.map { row ->
            "deleted ${row.id}|${row.title}|${time(row.deletedAt)}|${source(row.source)}|${row.deviceName.orEmpty()}|${row.entryId.orEmpty()}|${row.canRestore}"
        }
        return tasks + deleted
    }

    fun rows(case: FixtureCase): String? {
        val expected = pcLines(case.expected.jsonObject)
        val actual = phoneLines(ArchiveBuilder.build(input(case)))
        if (expected == actual) return null
        if (expected.size != actual.size) return "C# ${expected.size} lines, kotlin ${actual.size}: C# $expected vs kotlin $actual"
        val at = expected.indices.first { expected[it] != actual[it] }
        return "line $at: C# ${expected[at]} vs kotlin ${actual[at]}"
    }
}
