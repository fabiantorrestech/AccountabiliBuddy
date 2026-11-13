package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.SyncTaskLogItem
import com.accountabilibuddy.shared.tasks.SyncTaskView
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.OffsetDateTime

/** Reading the R7.1 fixtures' inputs (the PC's JSON, as written) and comparing the phone's output with the PC's, byte for byte. */
object R71Json {
    fun time(text: String): OffsetDateTime = DotNetTime.cut(OffsetDateTime.parse(text))

    fun time(case: FixtureCase, key: String = "now"): OffsetDateTime = time(case.text(key))

    /** An input value, null when it is missing or null. */
    fun value(case: FixtureCase, key: String): JsonElement? = case.input[key]?.takeIf { it !is JsonNull }

    fun textOrNull(case: FixtureCase, key: String): String? = value(case, key)?.jsonPrimitive?.content

    fun texts(json: JsonElement?): List<String>? = json?.takeIf { it !is JsonNull }?.jsonArray?.map { it.jsonPrimitive.content }

    // The lists as written (not cleaned): what a store holds.
    fun tasks(json: JsonElement): List<SyncTaskItem> = json.jsonArray.map(TaskJson::readSyncTask)

    fun labels(json: JsonElement): List<SyncTaskLabel> = json.jsonArray.map(TaskJson::readSyncLabel)

    fun views(json: JsonElement): List<SyncTaskView> = json.jsonArray.map(TaskJson::readSyncView)

    fun log(json: JsonElement): List<SyncTaskLogItem> = json.jsonArray.map(TaskJson::readSyncLogItem)

    fun tasksJson(items: List<SyncTaskItem>): JsonArray = JsonArray(items.map { TaskJson.writeSyncTask(it) })

    /** The PC's name of a field ("CantMiss") as the phone's. */
    fun field(name: String): TaskField = TaskField.entries.first { it.wire.equals(name, ignoreCase = true) }

    /** Null when [actual] is the PC's `expected[key]` and its text is `expected.text`, byte for byte. */
    fun same(expected: JsonElement, key: String, actual: JsonElement): String? {
        val wanted = expected.jsonObject
        return JsonParity.difference(wanted.getValue(key), actual)
            ?: GoalCaseRunner.sameText(wanted.getValue("text").jsonPrimitive.content, TaskJson.text(actual))
    }

    /** Every object key starting with "z" under [json] (the fixtures' unknown fields), with repeats. */
    fun unknownNames(json: JsonElement): List<String> = when (json) {
        is JsonObject -> json.entries.flatMap { (key, value) -> (if (key.startsWith("z")) listOf(key) else emptyList()) + unknownNames(value) }
        is JsonArray -> json.flatMap(::unknownNames)
        is JsonPrimitive -> emptyList()
    }
}
