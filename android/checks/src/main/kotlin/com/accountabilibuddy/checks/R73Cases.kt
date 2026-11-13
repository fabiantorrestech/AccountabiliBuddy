package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskPageOrder
import com.accountabilibuddy.shared.tasks.TaskViewContext
import com.accountabilibuddy.shared.tasks.TaskViewRules
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.ZoneId

/**
 * The R7.3a parity cases: what a view shows ([TaskViewRules] against the PC's Core TaskViewRules.Apply: the groups, their keys and
 * titles and the tasks in each, in order) and the page order ([TaskPageOrder] against the PC's App TaskColumnOrder).
 */
object R73Cases {
    private fun text(json: JsonElement?): String? = json?.takeIf { it !is JsonNull }?.jsonPrimitive?.content

    private fun bin(wire: String): TaskBin = TaskBin.entries.first { it.wire == wire }

    fun viewRules(case: FixtureCase): String? {
        val tasks = R71Json.tasks(case.input.getValue("tasks")).map { it.task }
        val labels = R71Json.labels(case.input.getValue("labels")).map { it.label }
        val config = TaskJson.readView(case.input.getValue("view")).config
        val scope = R71Json.texts(case.input["scope"])?.map(::bin)
        val context = TaskViewContext(R71Json.time(case), ZoneId.of(case.text("zone")), labels, null, scope, case.input.getValue("showEmptyBins").jsonPrimitive.boolean)
        val result = TaskViewRules.apply(tasks, config, context)
        val expected = case.expected.jsonObject
        val groups = expected.getValue("groups").jsonArray.map { it.jsonObject }
        if (groups.size != result.groups.size) return "groups: C# ${groups.map { text(it["key"]) }} vs kotlin ${result.groups.map { it.key }}"
        for ((index, wanted) in groups.withIndex()) {
            val got = result.groups[index]
            if (text(wanted["key"]) != got.key) return "group $index key: C# ${text(wanted["key"])} vs kotlin ${got.key}"
            if (text(wanted["title"]) != got.title) return "group ${got.key} title: C# ${text(wanted["title"])} vs kotlin ${got.title}"
            if (text(wanted["bin"]) != got.bin?.wire) return "group ${got.key} bin: C# ${text(wanted["bin"])} vs kotlin ${got.bin?.wire}"
            if (text(wanted["labelId"]) != got.labelId?.lowercase()) return "group ${got.key} label: C# ${text(wanted["labelId"])} vs kotlin ${got.labelId}"
            if (text(wanted["tag"]) != got.tag) return "group ${got.key} tag: C# ${text(wanted["tag"])} vs kotlin ${got.tag}"
            val ids = R71Json.texts(wanted["ids"]).orEmpty()
            val gotIds = got.tasks.map { it.id.lowercase() }
            if (ids != gotIds) return "group ${got.key} tasks: C# ${ids.map { it.takeLast(2) }} vs kotlin ${gotIds.map { it.takeLast(2) }}"
        }

        val count = expected.getValue("count").jsonPrimitive.int
        return if (count == result.count) null else "count: C# $count vs kotlin ${result.count}"
    }

    fun columnOrder(case: FixtureCase): String? {
        val shown = R71Json.texts(case.input["shown"]).orEmpty()
        val stored = R71Json.texts(case.input["stored"])
        val expected = case.expected.takeIf { it !is JsonNull }
        return when (case.text("op")) {
            "apply" -> same(R71Json.texts(expected), TaskPageOrder.apply(shown, stored) { it })
            "movedBy" -> same(R71Json.texts(expected), TaskPageOrder.movedBy(shown, stored, case.text("key"), case.input.getValue("step").jsonPrimitive.int))
            "movedOnto" -> same(R71Json.texts(expected), TaskPageOrder.movedOnto(shown, stored, case.text("key"), case.text("target")))
            "groupingOf" -> TaskPageOrder.groupingOf(case.text("group")).let { got -> if (text(expected) == got) null else "C# ${text(expected)} vs kotlin $got" }
            else -> "unknown op ${case.text("op")}"
        }
    }

    private fun same(expected: List<String>?, actual: List<String>?): String? = if (expected == actual) null else "C# $expected vs kotlin $actual"
}
