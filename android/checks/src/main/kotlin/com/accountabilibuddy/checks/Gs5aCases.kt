package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalStatus
import com.accountabilibuddy.shared.goals.GoalTaskLink
import com.accountabilibuddy.shared.goals.GoalTaskLinks
import com.accountabilibuddy.shared.tasks.TaskLists
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate
import java.util.UUID

/** GS5b: the task goal link's counting and link reading on the phone, case by case against the PC's `gs5a/counting.json` and `links.json`. */
object Gs5aCases {
    /** What a goal counts from the focus of its linked tasks (and a milestone's finished tasks), the PC's `GoalCounting.Day`. */
    fun counting(case: FixtureCase): String? {
        val world = GoalFixtureWorld(case.input)
        val links = (case.input["links"] as? JsonArray).orEmpty().map(::link)
        val data = world.data().copy(taskLinks = links)
        val expected = case.expected.jsonObject
        if (expected.containsKey("values")) {
            return milestone(case, world, data, expected)
        }

        val state = GoalCounting.day(world.goal, data, world.today, world.today)
        val actual = buildJsonObject {
            put("minutes", GoalSnapshotJson.num(state.minutes))
            put("value", GoalSnapshotJson.num(state.value))
            put("sources", JsonArray(state.sources.map { JsonPrimitive(it.name) }))
            put("hit", state.hit)
        }
        return JsonParity.difference(expected, actual)
    }

    // The milestone case: one value per listed day, and the Target tile's current / target.
    private fun milestone(case: FixtureCase, world: GoalFixtureWorld, data: com.accountabilibuddy.shared.goals.GoalSourceData, expected: JsonObject): String? {
        val days = case.input.getValue("days").jsonArray.map { LocalDate.parse(it.jsonPrimitive.content) }
        val target = GoalStatus.target(world.goal, data, world.today)
        val actual = buildJsonObject {
            put("values", JsonArray(days.map { GoalSnapshotJson.num(GoalCounting.day(world.goal, data, it, world.today).value) }))
            put("target", target?.let { buildJsonObject { put("current", GoalSnapshotJson.num(it.current)); put("target", GoalSnapshotJson.num(it.target)) } } ?: JsonNull)
        }
        return JsonParity.difference(expected, actual)
    }

    /** `GoalTaskLinks.From` over the PC's tasks and log. */
    fun links(case: FixtureCase): String? {
        val lists = TaskLists(R71Json.tasks(case.input.getValue("tasks")), emptyList(), emptyList(), R71Json.log(case.input.getValue("log")))
        val actual = JsonArray(GoalTaskLinks.from(lists).map(::linkJson))
        return JsonParity.difference(case.expected, actual)
    }

    fun link(json: JsonElement): GoalTaskLink {
        val obj = json.jsonObject
        return GoalTaskLink(
            UUID.fromString(obj.getValue("taskId").jsonPrimitive.content), UUID.fromString(obj.getValue("goalId").jsonPrimitive.content),
            obj.getValue("title").jsonPrimitive.content, obj["doneAt"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content?.let(GoalJson::parseTime),
        )
    }

    private fun linkJson(link: GoalTaskLink): JsonObject = buildJsonObject {
        put("taskId", link.taskId.toString())
        put("goalId", link.goalId.toString())
        put("title", link.title)
        put("doneAt", link.doneAt?.let(GoalJson::timeValue) ?: JsonNull)
        put("isDone", link.isDone)
    }
}
