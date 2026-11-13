package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.TaskAccountSettingsMerge
import com.accountabilibuddy.shared.tasks.TaskEdits
import com.accountabilibuddy.shared.tasks.TaskFieldMerge
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabelMerge
import com.accountabilibuddy.shared.tasks.TaskListMerge
import com.accountabilibuddy.shared.tasks.TaskNumbering
import com.accountabilibuddy.shared.tasks.TaskRingDevices
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * R7.1: the phone's merges against the PC's, on the PC's own inputs and outputs: tasks field by field (each scenario both ways and
 * again), a local change's stamps, labels with "Rings on", the account's Tasks settings, views and History, the ring lists and the
 * numbering rule.
 */
object R71MergeCases {
    fun fieldMerge(case: FixtureCase): String? {
        val merged = TaskFieldMerge.merge(
            R71Json.tasks(case.input.getValue("first")), R71Json.tasks(case.input.getValue("second")), R71Json.time(case), R71Json.textOrNull(case, "self"),
        )
        return R71Json.same(case.expected, "list", R71Json.tasksJson(merged))
    }

    fun taskEdits(case: FixtureCase): String? {
        val item = TaskJson.readSyncTask(case.input.getValue("item"))
        val now = R71Json.time(case)
        val by = case.text("by")
        val result = if (case.text("kind") == "edit") {
            TaskEdits.edit(item, R71Json.field(case.text("field")), TaskJson.readTask(case.input.getValue("changed")), now, by)
        } else {
            val conflict = item.task.conflicts!![case.input.getValue("conflict").jsonPrimitive.int]
            TaskEdits.resolve(item, conflict, R71Json.textOrNull(case, "kept"), now, by)
        }

        return R71Json.same(case.expected, "item", TaskJson.writeSyncTask(result))
    }

    fun labelMerge(case: FixtureCase): String? {
        val merged = TaskLabelMerge.merge(R71Json.labels(case.input.getValue("held")), R71Json.labels(case.input.getValue("incoming")), R71Json.time(case))
        return R71Json.same(case.expected, "list", JsonArray(merged.map { TaskJson.writeSyncLabel(it) }))
    }

    fun accountSettings(case: FixtureCase): String? {
        val first = R71Json.value(case, "first")?.let(TaskJson::readAccountSettingsObject)
        val second = R71Json.value(case, "second")?.let(TaskJson::readAccountSettingsObject)
        val merged = TaskAccountSettingsMerge.merge(first, second, R71Json.time(case))
        return R71Json.same(case.expected, "settings", TaskJson.writeAccountSettings(merged))
    }

    fun listsMerge(case: FixtureCase): String? {
        val now = R71Json.time(case)
        val held = case.input.getValue("held")
        val incoming = case.input.getValue("incoming")
        val merged = if (case.text("kind") == "views") {
            JsonArray(TaskListMerge.views(R71Json.views(held), R71Json.views(incoming), now).map { TaskJson.writeSyncView(it) })
        } else {
            JsonArray(TaskListMerge.taskLog(R71Json.log(held), R71Json.log(incoming), now).map { TaskJson.writeSyncLogItem(it) })
        }

        return R71Json.same(case.expected, "list", merged)
    }

    fun ringDevices(case: FixtureCase): String? {
        fun ids(key: String) = R71Json.texts(case.input[key])
        fun json(ids: List<String>?) = ids?.let { list -> JsonArray(list.map { JsonPrimitive(it) }) } ?: JsonNull
        return when (case.text("kind")) {
            "text" -> {
                val expected = case.expected.jsonObject
                val text = TaskRingDevices.text(ids("ringOn"))
                if (text != expected.getValue("text").jsonPrimitive.content) "text: C# ${expected["text"]} vs kotlin $text"
                else JsonParity.difference(expected.getValue("normalized"), json(TaskRingDevices.normalized(ids("ringOn"))))
            }

            "ringsFor" -> {
                val actual = TaskRingDevices.ringsFor(ids("account"), ids("label"), case.text("device"), case.input.getValue("isPc").jsonPrimitive.boolean)
                if (actual == case.expected.jsonPrimitive.boolean) null else "C# ${case.expected} vs kotlin $actual"
            }

            else -> JsonParity.difference(
                case.expected.jsonObject.getValue("ringOn"),
                json(TaskRingDevices.toggled(ids("ringOn"), ids("phones")!!, case.text("device"), case.input.getValue("on").jsonPrimitive.boolean)),
            )
        }
    }

    fun numbering(case: FixtureCase): String? {
        if (case.text("kind") == "device") {
            val devices = case.input.getValue("devices").jsonArray.map { it.jsonObject.getValue("id").jsonPrimitive.content to it.jsonObject.getValue("kind").jsonPrimitive.content }
            val actual = TaskNumbering.numberingDevice(devices)
            val expected = (case.expected as? JsonPrimitive)?.takeIf { it !is JsonNull }?.content
            return if (actual == expected) null else "C# $expected vs kotlin $actual"
        }

        val result = TaskNumbering.assignFinal(
            R71Json.tasks(case.input.getValue("tasks")), case.text("self"), R71Json.textOrNull(case, "numberingDevice"), R71Json.time(case),
            case.input.getValue("alsoUsed").jsonArray.map(TaskJson::readTask),
        )
        return R71Json.same(case.expected, "list", R71Json.tasksJson(result))
    }
}
