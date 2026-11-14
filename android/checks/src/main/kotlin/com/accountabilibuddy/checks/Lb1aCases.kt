package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.SyncTagScope
import com.accountabilibuddy.shared.tasks.TagScopeEdits
import com.accountabilibuddy.shared.tasks.TagScopeMerge
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLabelMerge
import com.accountabilibuddy.shared.tasks.TaskListMerge
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskScopes
import com.accountabilibuddy.shared.tasks.TaskSearch
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * The LB1a parity cases (area `lb1`, written by the C# checks' `Lb1aFixtures`, ADR 89): the phone's twins of the PC's device-only label and
 * tag rules over the same made-up labels, scopes and devices. Lists are compared as the wire writes them (names in order, nulls left out) and,
 * where the fixture has the PC's text, byte for byte.
 */
object Lb1aCases {
    private fun ids(json: JsonElement?): List<String>? = json?.takeIf { it !is JsonNull }?.jsonArray?.map { it.jsonPrimitive.content }

    private fun scopes(json: JsonElement?): List<SyncTagScope> = json?.takeIf { it !is JsonNull }?.jsonArray.orEmpty().map(TaskJson::readSyncTagScope)

    private fun scopesJson(list: List<SyncTagScope>?): JsonArray = TaskJson.writeTagScopes(list.orEmpty())

    private fun nullable(json: JsonElement?): String? = json?.takeIf { it !is JsonNull }?.jsonPrimitive?.content

    fun labelMerge(case: FixtureCase): String? {
        val merged = TaskLabelMerge.merge(R71Json.labels(case.input.getValue("held")), R71Json.labels(case.input.getValue("incoming")), R71Json.time(case))
        return R71Json.same(case.expected, "list", JsonArray(merged.map { TaskJson.writeSyncLabel(it) }))
    }

    fun tagScopeMerge(case: FixtureCase): String? {
        val merged = TagScopeMerge.merge(scopes(case.input["held"]), scopes(case.input["incoming"]), R71Json.time(case))
        val expected = case.expected.jsonObject
        if ((merged == null) != expected.getValue("none").jsonPrimitive.boolean) return "C# none=${expected["none"]} vs kotlin ${merged?.size}"
        return R71Json.same(case.expected, "list", scopesJson(merged))
    }

    fun visibility(case: FixtureCase): String? {
        val expected = case.expected.jsonObject
        if (case.text("kind") == "ids") {
            val tag = case.text("tag")
            return if (expected.getValue("tagId").jsonPrimitive.content == TaskScopes.tagIdFor(tag) && expected.getValue("labelId").jsonPrimitive.content == TaskLabel.idFor(tag)) {
                null
            } else {
                "C# tag ${expected["tagId"]} label ${expected["labelId"]} vs kotlin ${TaskScopes.tagIdFor(tag)} ${TaskLabel.idFor(tag)}"
            }
        }

        val onlyOn = ids(case.input["onlyOn"])
        val self = case.text("self")
        val removed = ids(case.input["removed"]).orEmpty()
        val got = JsonObject(
            mapOf(
                "visible" to JsonPrimitive(TaskScopes.isVisible(onlyOn, self, removed)),
                "shared" to JsonPrimitive(TaskScopes.isShared(onlyOn, removed)),
                "effective" to (TaskScopes.effective(onlyOn, removed)?.let { list -> JsonArray(list.map { JsonPrimitive(it) }) } ?: JsonNull),
                "normalized" to (TaskScopes.normalized(onlyOn)?.let { list -> JsonArray(list.map { JsonPrimitive(it) }) } ?: JsonNull),
            ),
        )
        return JsonParity.difference(case.expected, got)
    }

    fun withHidden(case: FixtureCase): String? {
        val self = case.text("self")
        val removed = ids(case.input["removed"]).orEmpty()
        if (case.text("kind") == "tags") {
            val got = TaskScopes.withHiddenTags(ids(case.input["shownEdit"]), ids(case.input["held"]), scopes(case.input["scopes"]), self, removed)
            return JsonParity.difference(case.expected, got?.let { list -> JsonArray(list.map { JsonPrimitive(it) }) } ?: JsonNull)
        }

        val labels = R71Json.labels(case.input.getValue("labels")).map { it.label }
        val got = TaskScopes.withHiddenLabel(nullable(case.input["chosen"]), nullable(case.input["held"]), labels, self, removed)
        return JsonParity.difference(case.expected, got?.let { JsonPrimitive(it) } ?: JsonNull)
    }

    fun labelAdd(case: FixtureCase): String? {
        val lists = TaskLists(emptyList(), R71Json.labels(case.input.getValue("labels")), emptyList(), emptyList())
        val (result, id) = TaskLabelEdits.addWithId(
            lists, case.text("name"), null, R71Json.time(case), case.text("self"), case.input.getValue("onlyOnThisDevice").jsonPrimitive.boolean, ids(case.input["removed"]).orEmpty(),
        )
        val expectedId = nullable(case.expected.jsonObject["id"])
        if (expectedId != id) return "C# id $expectedId vs kotlin $id"
        return R71Json.same(case.expected, "labels", JsonArray(result.labels.map { TaskJson.writeSyncLabel(it) }))
    }

    fun labelSetOnlyOn(case: FixtureCase): String? {
        val lists = TaskLists(emptyList(), R71Json.labels(case.input.getValue("labels")), emptyList(), emptyList())
        val result = TaskLabelEdits.setOnlyOn(lists, case.text("labelId"), ids(case.input["devices"]), R71Json.time(case), case.text("self"))
        return R71Json.same(case.expected, "labels", JsonArray(result.labels.map { TaskJson.writeSyncLabel(it) }))
    }

    fun checkName(case: FixtureCase): String? {
        val lists = TaskLists(emptyList(), R71Json.labels(case.input.getValue("labels")), emptyList(), emptyList())
        val got = TaskLabelEdits.checkName(lists, nullable(case.input["labelId"]), case.text("name"), case.text("self"), ids(case.input["removed"]).orEmpty())
        val expected = case.expected.jsonPrimitive.content
        return if (got.name.equals(expected, ignoreCase = true)) null else "C# $expected vs kotlin $got"
    }

    fun tagScopeEdits(case: FixtureCase): String? {
        val result = TagScopeEdits.setScope(
            scopes(case.input["scopes"]), case.text("tag"), case.input.getValue("onlyOnThisDevice").jsonPrimitive.boolean, R71Json.time(case), case.text("self"),
        )
        return R71Json.same(case.expected, "scopes", scopesJson(result))
    }

    /** A file's `tagScopes` part read leniently and cleaned, written again: the PC's text. */
    fun cleanTagScopes(case: FixtureCase): String? {
        val read = TaskJson.readTagScopes(case.input.getValue("json"))
        val expected = case.expected.jsonObject
        val absent = expected["absent"] != null
        if (read == null || absent) {
            return if (read == null && absent) null else "the PC reads it as ${if (absent) "absent" else "present"}, the phone as ${if (read == null) "absent" else "present"}"
        }

        return R71Json.same(case.expected, "json", scopesJson(read))
    }

    // The lists' parts the fix rules touch (tasks, labels, scopes), as the fixtures write them.
    private fun listsOf(json: JsonElement): TaskLists {
        val obj = json.jsonObject
        val scopes = scopes(obj["scopes"]).ifEmpty { null }
        return TaskLists(R71Json.tasks(obj.getValue("tasks")), R71Json.labels(obj.getValue("labels")), emptyList(), emptyList(), null, scopes)
    }

    private fun sameLists(expected: JsonElement, actual: TaskLists): String? {
        val obj = expected.jsonObject
        return JsonParity.difference(obj.getValue("tasks"), R71Json.tasksJson(actual.tasks))
            ?: JsonParity.difference(obj.getValue("labels"), JsonArray(actual.labels.map { TaskJson.writeSyncLabel(it) }))
            ?: JsonParity.difference(obj.getValue("scopes"), scopesJson(actual.tagScopes))
    }

    /** A device never loses sight of its own label (ADR 89): the labels and scopes after [TaskListMerge.mergeLists]. */
    fun mergeListsRejoin(case: FixtureCase): String? {
        val held = TaskLists(emptyList(), R71Json.labels(case.input.getValue("heldLabels")), emptyList(), emptyList(), null, scopes(case.input["heldScopes"]).ifEmpty { null })
        val incoming = TaskLists(emptyList(), R71Json.labels(case.input.getValue("incomingLabels")), emptyList(), emptyList(), null, scopes(case.input["incomingScopes"]).ifEmpty { null })
        val result = TaskListMerge.mergeLists(held, incoming, case.text("self"), R71Json.time(case))
        val expected = case.expected.jsonObject
        return JsonParity.difference(expected.getValue("labels"), JsonArray(result.labels.map { TaskJson.writeSyncLabel(it) })) ?: JsonParity.difference(expected.getValue("scopes"), scopesJson(result.tagScopes))
    }

    /** Three devices passing lists around in several orders end with the same lists as the PC. */
    fun converge(case: FixtureCase): String? {
        val ids = case.input.getValue("ids").jsonObject.mapValues { it.value.jsonPrimitive.content }
        val devices = mutableMapOf(
            "A" to TaskLists(emptyList(), R71Json.labels(case.input.getValue("a")), emptyList(), emptyList(), null, scopes(case.input["scopesA"])),
            "B" to TaskLists(emptyList(), R71Json.labels(case.input.getValue("b")), emptyList(), emptyList(), null, scopes(case.input["scopesB"])),
            "C" to TaskLists(emptyList(), R71Json.labels(case.input.getValue("c")), emptyList(), emptyList()),
        )
        val start = R71Json.time(case)
        case.input.getValue("steps").jsonArray.forEachIndexed { step, pair ->
            val receiver = pair.jsonArray[0].jsonPrimitive.content
            val sender = pair.jsonArray[1].jsonPrimitive.content
            devices[receiver] = TaskListMerge.mergeLists(devices.getValue(receiver), devices.getValue(sender), ids.getValue(receiver), start.plusMinutes(step.toLong()))
        }

        for ((device, lists) in devices) {
            val expected = case.expected.jsonObject.getValue(device).jsonObject
            JsonParity.difference(expected.getValue("labels"), JsonArray(lists.labels.map { TaskJson.writeSyncLabel(it) }))?.let { return "$device: $it" }
            JsonParity.difference(expected.getValue("scopes"), scopesJson(lists.tagScopes))?.let { return "$device: $it" }
        }

        return null
    }

    /** Every writer of a task's tags or label ends with [TaskScopes.keepHidden]. */
    fun keepHidden(case: FixtureCase): String? {
        val result = TaskScopes.keepHidden(listsOf(case.input.getValue("before")), listsOf(case.input.getValue("after")), case.text("self"), ids(case.input["removed"]).orEmpty(), R71Json.time(case))
        return sameLists(case.expected, result)
    }

    /** A backup taken in as a restore ([TaskListRules.restoredIncoming]). */
    fun restoreIncoming(case: FixtureCase): String? {
        val result = TaskListRules.restoredIncoming(
            listsOf(case.input.getValue("current")), listsOf(case.input.getValue("backup")), nullable(case.input["device"]), case.text("self"), R71Json.time(case), ids(case.input["removed"]).orEmpty(),
        )
        return sameLists(case.expected, result)
    }

    fun search(case: FixtureCase): String? {
        val tasks = R71Json.tasks(case.input.getValue("tasks")).map { it.task }
        val labels = R71Json.labels(case.input.getValue("labels")).map { it.label }
        val scopes = scopes(case.input["scopes"])
        val self = case.text("self")
        val removed = ids(case.input["removed"]).orEmpty()
        val found = TaskSearch.matchingIds(tasks, case.text("query"), labels, TaskScopes.labelTest(self, removed), TaskScopes.tagTest(scopes, self, removed))
        val expected = case.expected.takeIf { it !is JsonNull }?.jsonArray?.map { it.jsonPrimitive.content }?.toSet()
        return when {
            expected == null && found == null -> null
            expected == null -> "C# shows all, kotlin finds ${found?.size}"
            found == null -> "C# finds ${expected.size}, kotlin shows all"
            expected != found -> "C# $expected vs kotlin $found"
            else -> null
        }
    }
}
