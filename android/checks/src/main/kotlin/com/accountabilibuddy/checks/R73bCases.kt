package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.ShelfReorder
import com.accountabilibuddy.shared.tasks.ShelfToastLength
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskTitleHelp
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long

/**
 * The R7.3b parity cases (area `r73`, written by the C# checks' `R73bFixtures`): the Add sheet's reading of a typed title
 * ([TaskTitleHelp.parse] against the PC's TaskTitleHelp.Parse), a reorder ([ShelfReorder.before] against the PC's
 * TaskActions.Reorder: the whole task list afterwards, stamps included) and the toasts' lives ([ShelfToasts.lengthOf] against the
 * PC's TaskToastTiming).
 */
object R73bCases {
    fun typedTitle(case: FixtureCase): String? {
        val labels = R71Json.labels(case.input.getValue("labels")).map { it.label }
        val typed = TaskTitleHelp.parse(case.text("text"), labels)
        val expected = case.expected.jsonObject
        val title = expected.getValue("title").jsonPrimitive.content
        val label = expected["labelId"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content
        val tags = R71Json.texts(expected["tags"]).orEmpty()
        return when {
            title != typed.title -> "title: C# \"$title\" vs kotlin \"${typed.title}\""
            label != typed.labelId?.lowercase() -> "label: C# $label vs kotlin ${typed.labelId}"
            tags != typed.tags -> "tags: C# $tags vs kotlin ${typed.tags}"
            else -> null
        }
    }

    fun reorder(case: FixtureCase): String? {
        val lists = TaskLists(R71Json.tasks(case.input.getValue("tasks")), emptyList(), emptyList(), emptyList())
        val result = ShelfReorder.before(lists, case.text("id"), R71Json.textOrNull(case, "before"), case.text("self"), R71Json.time(case))
        val expected = case.expected.jsonObject
        val changed = expected.getValue("changed").jsonPrimitive.boolean
        if (changed != result.changed) return "changed: C# $changed vs kotlin ${result.changed}"
        return JsonParity.difference(expected.getValue("tasks"), R71Json.tasksJson(result.lists.tasks))
    }

    fun toastTiming(case: FixtureCase): String? {
        val expected = case.expected.jsonObject
        val wanted = expected.getValue("length").jsonPrimitive.content
        val length = case.input["length"]?.let { name -> ShelfToastLength.entries.first { it.name.replace("_", "").equals(name.jsonPrimitive.content, ignoreCase = true) } }
            ?: ShelfToasts.lengthOf(case.input.getValue("canUndo").jsonPrimitive.boolean, case.input.getValue("explains").jsonPrimitive.boolean)
        return when {
            !length.name.replace("_", "").equals(wanted, ignoreCase = true) -> "length: C# $wanted vs kotlin ${length.name}"
            length.millis != expected.getValue("millis").jsonPrimitive.long -> "millis: C# ${expected.getValue("millis")} vs kotlin ${length.millis}"
            else -> null
        }
    }
}
