package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldValues
import com.accountabilibuddy.shared.tasks.TaskJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * R7.1: the task model's JSON against the PC's (`task-json`: ADR 76's parts read leniently, cleaned and written again, with the
 * BINDING round trip of a task with every field set, byte for byte) and the merge's building blocks (`values`: hashes, each
 * field's canonical text).
 */
object R71ModelCases {
    /**
     * A part read and written by the phone gives the PC's text. Where the PC drops something the phone keeps (`pcDrops`: unknown
     * fields, entries with a newer app's enum value), the phone's known part must still be the PC's text, and its full writing must
     * keep every unknown name and read back the same.
     */
    fun taskJson(case: FixtureCase): String? {
        val part = case.text("part")
        val json = case.input.getValue("json")
        val pcDrops = case.input["pcDrops"]?.jsonPrimitive?.booleanOrNull == true
        val expected = case.expected.jsonObject
        val absent = expected["absent"] != null

        if (part == "taskAccountSettings") {
            val read = TaskJson.readAccountSettings(json)
            if (read == null || absent) {
                return if (read == null && absent) null else "the PC reads it as ${if (absent) "absent" else "present"}, the phone as ${if (read == null) "absent" else "present"}"
            }

            R71Json.same(expected, "json", TaskJson.writeAccountSettings(read, unknown = !pcDrops))?.let { return it }
            return if (pcDrops) keepsUnknown(json, TaskJson.writeAccountSettings(read)) { TaskJson.writeAccountSettings(TaskJson.readAccountSettings(it)!!) } else null
        }

        val known: JsonArray?
        val full: JsonArray?
        val newer: Int
        when (part) {
            "tasks" -> TaskJson.readTasks(json).let { read ->
                known = read?.let { JsonArray(it.items.map { item -> TaskJson.writeSyncTask(item, unknown = false) }) }
                full = read?.let { TaskJson.writeTasks(it.items, it.newer) }
                newer = read?.newer?.size ?: 0
            }

            "labels" -> TaskJson.readLabels(json).let { read ->
                known = read?.let { JsonArray(it.items.map { item -> TaskJson.writeSyncLabel(item, unknown = false) }) }
                full = read?.let { TaskJson.writeLabels(it.items, it.newer) }
                newer = read?.newer?.size ?: 0
            }

            "views" -> TaskJson.readViews(json).let { read ->
                known = read?.let { JsonArray(it.items.map { item -> TaskJson.writeSyncView(item, unknown = false) }) }
                full = read?.let { TaskJson.writeViews(it.items, it.newer) }
                newer = read?.newer?.size ?: 0
            }

            "taskLog" -> TaskJson.readTaskLog(json).let { read ->
                known = read?.let { JsonArray(it.items.map { item -> TaskJson.writeSyncLogItem(item, unknown = false) }) }
                full = read?.let { TaskJson.writeTaskLog(it.items, it.newer) }
                newer = read?.newer?.size ?: 0
            }

            else -> return "unknown part $part"
        }

        if (known == null || full == null || absent) {
            return if (known == null && absent) null else "the PC reads it as ${if (absent) "absent" else "present"}, the phone as ${if (known == null) "absent" else "present"}"
        }

        if (!pcDrops) {
            // Nothing unknown: what the phone stores and sends is the PC's text, and nothing was set aside.
            if (newer != 0) return "the phone set $newer entries aside as a newer app's"
            return R71Json.same(expected, "json", full)
        }

        R71Json.same(expected, "json", known)?.let { return it }
        val skippedByPc = (json as JsonArray).size - known.size
        if (newer != skippedByPc) return "the PC skips $skippedByPc entries; the phone kept $newer as written"
        return keepsUnknown(json, full) { again -> reread(part, again) }
    }

    // The phone's full writing holds every unknown name of the input and reads back to the same JSON (a store round trip).
    private fun keepsUnknown(input: JsonElement, written: JsonElement, again: (JsonElement) -> JsonElement): String? {
        val wanted = R71Json.unknownNames(input).filter { it != "zPlan" }.sorted()
        val kept = R71Json.unknownNames(written).sorted()
        if (wanted != kept) return "unknown fields: the input has $wanted, the phone wrote back $kept"
        val second = again(written)
        return if (TaskJson.text(second) == TaskJson.text(written)) null else "reading the phone's own writing and writing it again changed it"
    }

    private fun reread(part: String, json: JsonElement): JsonElement = when (part) {
        "tasks" -> TaskJson.readTasks(json)!!.let { TaskJson.writeTasks(it.items, it.newer) }
        "labels" -> TaskJson.readLabels(json)!!.let { TaskJson.writeLabels(it.items, it.newer) }
        "views" -> TaskJson.readViews(json)!!.let { TaskJson.writeViews(it.items, it.newer) }
        else -> TaskJson.readTaskLog(json)!!.let { TaskJson.writeTaskLog(it.items, it.newer) }
    }

    /** A title's or note's hash, and every field's canonical text (what a full tie between two stamps is broken by). */
    fun values(case: FixtureCase): String? {
        if (case.text("kind") == "hash") {
            val actual = TaskFieldValues.hash(R71Json.textOrNull(case, "text"))
            val expected = case.expected.jsonPrimitive.content
            return if (actual == expected) null else "C# $expected vs kotlin $actual"
        }

        val task = TaskJson.readTask(case.input.getValue("task"))
        val expected = case.expected as JsonObject
        if (expected.size != TaskField.entries.size) return "the PC has ${expected.size} fields, the phone ${TaskField.entries.size}"
        for (field in TaskField.entries) {
            val name = expected.keys.firstOrNull { it.equals(field.wire, ignoreCase = true) } ?: return "the PC has no field ${field.wire}"
            GoalCaseRunner.sameText(expected.getValue(name).jsonPrimitive.content, TaskFieldValues.canonical(task, field))?.let { return "${field.wire}: $it" }
        }

        return null
    }
}
