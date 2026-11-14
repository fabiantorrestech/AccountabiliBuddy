package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncKeptPast
import com.accountabilibuddy.shared.tasks.ArchiveBuilder
import com.accountabilibuddy.shared.tasks.ArchiveComeback
import com.accountabilibuddy.shared.tasks.ArchiveFilter
import com.accountabilibuddy.shared.tasks.ArchiveList
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
            notes = json["notes"]?.let { SyncFileJson.readSessionNotes(it) }.orEmpty(),
        ).let { base ->
            val self = json["self"]?.jsonPrimitive?.content
            val names = json["deviceNames"]?.jsonObject?.mapValues { it.value.jsonPrimitive.content }.orEmpty()
            if (self == null) base else base.copy(self = self, deviceNames = names)
        }
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
            "deleted ${text(row, "id")}|${text(row, "title")}|${time(instant(text(row, "deletedAt")))}|${text(row, "source")}|${text(row, "deviceName")}|${text(row, "entryId")}|${row.getValue("canRestore").jsonPrimitive.content}|${text(row, "restoreAs")}|${row.getValue("canRestoreByStamp").jsonPrimitive.content}"
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
            "deleted ${row.id}|${row.title}|${time(row.deletedAt)}|${source(row.source)}|${row.deviceName.orEmpty()}|${row.entryId.orEmpty()}|${row.canRestore}|${row.restoreAs.orEmpty()}|${row.canRestoreByStamp}"
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

    // AR1c: the notes of ArchiveBuilder (AR1b, ADR 87) against the PC's: every device's live note newest first, "from <device>", a removed device's note stays.
    fun notes(case: FixtureCase): String? {
        val json = case.input
        val names = json.getValue("deviceNames").jsonObject.mapValues { it.value.jsonPrimitive.content }
        val input = ArchiveInput(
            emptyList(), emptyList(), emptyList(), emptyList(), TaskLists.EMPTY, Instant.parse("2026-11-13T12:00:00Z"), emptySet(),
            SyncFileJson.readSessionNotes(json.getValue("notes")).orEmpty(), text(json, "self"), names,
        )
        val expected = case.expected.jsonObject.getValue("notes").jsonArray.map { element ->
            val row = element.jsonObject
            "${text(row, "id")}|${text(row, "text")}|${time(instant(text(row, "at")))}|${text(row, "by")}|${text(row, "deviceName")}|${row.getValue("fromOther").jsonPrimitive.content}|${text(row, "fromText")}"
        }
        val actual = ArchiveBuilder.build(input).notes.map { row ->
            "${row.id}|${row.text}|${time(row.at)}|${row.by}|${row.deviceName.orEmpty()}|${row.fromOther}|${row.fromText.orEmpty()}"
        }
        if (expected == actual) return null
        return "C# $expected vs kotlin $actual"
    }

    // AR1c fix: the phone's whole list (ArchiveList.items / shown / summary / deleted over the builder) against the PC's real NotebookViewModel, written by the
    // C# AppChecks (Ar1cListFixtures): for each filter and two searches, every line's kind, text, time text and sort in the PC's order, the summary and Recently deleted.
    fun list(case: FixtureCase): String? {
        val rows = ArchiveBuilder.build(input(case))
        val comebacks = case.input.getValue("comebacks").jsonArray.map { element ->
            val c = element.jsonObject
            ArchiveComeback(text(c, "id"), text(c, "text"), instant(text(c, "createdAt")), text(c, "doneAt").takeIf { it.isNotEmpty() }?.let(::instant))
        }
        val whenText = { time: Instant -> ArchiveList.whenText(time, java.time.ZoneOffset.UTC, java.util.Locale.US) }
        val items = ArchiveList.items(rows, comebacks, true, { _, stored -> stored }, whenText)
        fun line(kind: String, text: String, whenText: String, sort: Instant) = "$kind|$text|$whenText|$sort"
        val problems = ArrayList<String>()
        for (viewElement in case.expected.jsonObject.getValue("views").jsonArray) {
            val view = viewElement.jsonObject
            val filter = ArchiveFilter.entries.first { it.label == text(view, "filter") }
            val search = text(view, "search")
            val expected = view.getValue("items").jsonArray.map { element ->
                val raw = element.jsonPrimitive.content
                val cut = raw.lastIndexOf('|')
                raw.substring(0, cut) + "|" + instant(raw.substring(cut + 1))
            }
            val shown = ArchiveList.shown(items, filter, search)
            val actual = shown.map { line(it.kind.label, it.text, it.whenText, it.sort) }
            val where = "${filter.label} / '$search'"
            if (expected != actual) problems.add("$where: PC $expected vs phone $actual")
            if (text(view, "summary") != ArchiveList.summary(items)) problems.add("$where summary: PC ${text(view, "summary")} vs phone ${ArchiveList.summary(items)}")
            val deleted = ArchiveList.deleted(rows, search, whenText)
            val expectedDeleted = view.getValue("deleted").jsonArray.map { it.jsonPrimitive.content }
            val actualDeleted = deleted.map { "${it.text}|${it.row.title}" }
            if (expectedDeleted != actualDeleted) problems.add("$where deleted: PC $expectedDeleted vs phone $actualDeleted")
            if (text(view, "deletedHeader") != ArchiveList.deletedHeader(deleted.size)) problems.add("$where deleted header: PC ${text(view, "deletedHeader")} vs phone ${ArchiveList.deletedHeader(deleted.size)}")
        }

        return problems.firstOrNull()
    }
}
