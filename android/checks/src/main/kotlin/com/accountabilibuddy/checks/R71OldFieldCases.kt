package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.SyncComeback
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.OldTaskFieldsWriter
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskMigration
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPreset
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * R7.1: the old task fields and the migration rule against the PC's: the old lists rebuilt from the task list
 * ([OldTaskFieldsWriter]), the phone's own reader and writer of the old lists on the PC's JSON ([SyncFileJson]), and the one-time
 * migration with its counts ([TaskMigration]).
 */
object R71OldFieldCases {
    fun write(case: FixtureCase): String? {
        val fields = OldTaskFieldsWriter.write(
            R71Json.tasks(case.input.getValue("tasks")), R71Json.labels(case.input.getValue("labels")), R71Json.log(case.input.getValue("history")),
            R71Json.time(case), ZoneId.of(case.text("zone")), case.input.getValue("stub").jsonPrimitive.boolean,
        )
        val expected = case.expected.jsonObject

        // The same items as the phone reads the PC's lists...
        if (fields.nextTasks != SyncFileJson.readNextTasks(expected["nextTasks"])) return "nextTasks differ from the PC's"
        if (fields.later != SyncFileJson.readLater(expected["later"])) return "later differs from the PC's"
        if (fields.parked != SyncFileJson.readParked(expected["parked"])) return "parked differs from the PC's"
        if (fields.autoSaved != SyncFileJson.readAutoSaved(expected["autoSaved"])) return "autoSaved differs from the PC's"
        if (fields.categories != SyncFileJson.readCategories(expected["categories"])) return "categories differ from the PC's"
        // ...and the same JSON as the PC reads the phone's.
        return OldFieldJson.difference(expected, json(fields))
    }

    /** The PC's old lists read by the phone and written again: the PC reads the same from them (the Next marker included). */
    fun json(case: FixtureCase): String? {
        val input = case.input
        val read = OldTaskFields(
            SyncFileJson.readNextTasks(input["nextTasks"])!!, SyncFileJson.readLater(input["later"])!!, SyncFileJson.readParked(input["parked"])!!,
            comebacks(input.getValue("comebacks").jsonArray), SyncFileJson.readCategories(input["categories"])!!, SyncFileJson.readAutoSaved(input["autoSaved"])!!,
        )
        val sizes = listOf("nextTasks", "later", "parked", "autoSaved", "categories").map { input.getValue(it).jsonArray.size }
        if (sizes != listOf(read.nextTasks.size, read.later.size, read.parked.size, read.autoSaved.size, read.categories.size)) return "the phone left entries out: $sizes"
        return OldFieldJson.difference(case.expected, json(read))
    }

    fun migration(case: FixtureCase): String? {
        val input = case.input
        val result = TaskMigration.run(
            TaskMigrationInput(
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
                R71Json.time(case), ZoneId.of(case.text("zone")), input.getValue("isNumberingDevice").jsonPrimitive.boolean,
                input.getValue("firstNumber").jsonPrimitive.int, input.getValue("sessionActions").jsonPrimitive.int,
            ),
        )
        val expected = case.expected.jsonObject
        fun text(key: String) = expected.getValue(key).jsonPrimitive.content
        GoalCaseRunner.sameText(text("tasks"), TaskJson.text(TaskJson.writeTasks(result.tasks)))?.let { return "tasks: $it" }
        GoalCaseRunner.sameText(text("labels"), TaskJson.text(TaskJson.writeLabels(result.labels)))?.let { return "labels: $it" }
        GoalCaseRunner.sameText(text("views"), TaskJson.text(TaskJson.writeViews(result.views)))?.let { return "views: $it" }
        GoalCaseRunner.sameText(text("history"), TaskJson.text(TaskJson.writeTaskLog(result.history)))?.let { return "history: $it" }

        val searches = expected.getValue("searches").jsonObject.mapValues { it.value.jsonPrimitive.content }
        if (searches != result.viewSearches) return "searches: C# $searches vs kotlin ${result.viewSearches}"

        val counts = result.counts
        val actual = buildJsonObject {
            put("sources", JsonObject(counts.sources.mapValues { kotlinx.serialization.json.JsonPrimitive(it.value) }))
            put("sourceTombstones", JsonObject(counts.sourceTombstones.mapValues { kotlinx.serialization.json.JsonPrimitive(it.value) }))
            put("bins", JsonObject(counts.bins.entries.associate { it.key.wire to kotlinx.serialization.json.JsonPrimitive(it.value) }))
            put("history", counts.history)
            put("expired", counts.expired)
            put("duplicates", counts.duplicates)
            put("tombstones", counts.tombstones)
            put("labels", counts.labels)
            put("labelsFromItems", counts.labelsFromItems)
            put("views", counts.views)
            put("unmappedStatuses", counts.unmappedStatuses)
            put("balanced", counts.isBalanced)
        }
        return JsonParity.difference(expected.getValue("counts"), actual)
    }

    internal fun json(fields: OldTaskFields): JsonObject = buildJsonObject {
        put("nextTasks", SyncFileJson.writeNextTasks(fields.nextTasks))
        put("later", SyncFileJson.writeLater(fields.later))
        put("parked", SyncFileJson.writeParked(fields.parked))
        put(
            "comebacks",
            JsonArray(
                fields.comebacks.map { comeback ->
                    buildJsonObject {
                        put("id", comeback.id)
                        put("text", comeback.text)
                        put("createdAt", comeback.createdAt.toString())
                    }
                },
            ),
        )
        put("categories", SyncFileJson.writeCategories(fields.categories))
        put("autoSaved", SyncFileJson.writeAutoSaved(fields.autoSaved))
    }

    private fun comebacks(array: JsonArray): List<SyncComeback> = array.map { it.jsonObject }.map { item ->
        SyncComeback(
            item.getValue("id").jsonPrimitive.content, item.getValue("text").jsonPrimitive.content,
            OffsetDateTime.parse(item.getValue("createdAt").jsonPrimitive.content).toInstant(),
        )
    }
}
