package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.tasks.TaskSearch
import com.accountabilibuddy.shared.tasks.TaskViewIcons
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive

/**
 * The PT1 parity cases (area `pt1`): the search grammar ([TaskSearch.matchingIds] against the PC's Core TaskSearch.MatchingTaskIds,
 * written by the C# checks' `Pt1Fixtures`) and the view icons' names (the PC editor's list, written by the AppChecks program).
 */
object Pt1Cases {
    fun search(case: FixtureCase): String? {
        val tasks = R71Json.tasks(case.input.getValue("tasks")).map { it.task }
        val labels = R71Json.labels(case.input.getValue("labels")).map { it.label }
        val found = TaskSearch.matchingIds(tasks, case.text("query"), labels)
        val expected = case.expected.takeIf { it !is JsonNull }?.jsonArray?.map { it.jsonPrimitive.content }?.toSet()
        return when {
            expected == null && found == null -> null
            expected == null -> "C# shows all, kotlin finds ${found?.size}"
            found == null -> "C# finds ${expected.size}, kotlin shows all"
            expected != found -> "C# $expected vs kotlin $found"
            else -> null
        }
    }

    fun viewIcons(case: FixtureCase): String? {
        val names = case.input.getValue("names").jsonArray.map { it.jsonPrimitive.content }
        return if (names == TaskViewIcons.NAMES) null else "PC editor $names vs phone ${TaskViewIcons.NAMES}"
    }
}
