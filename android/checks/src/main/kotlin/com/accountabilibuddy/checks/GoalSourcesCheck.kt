package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLowering
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalSources
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * GoalSources.availabilityOf for every source type, and per goal kind × (a goal / Deep work by its flag / Deep work by its id):
 * allowedFor every source type, countedFor and GoalLowering.canPauseOrDelete, against the PC.
 */
class GoalSourcesCheck : FixtureCheck {
    override val area = "goals"
    override val name = "sources"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val goalJson = case.input["goal"]
                val actual = if (goalJson == null) {
                    buildJsonObject { GoalSourceType.entries.forEach { put(it.name, GoalSources.availabilityOf(it).name) } }
                } else {
                    val goal = GoalJson.readGoal(goalJson)
                    buildJsonObject {
                        put("allowedFor", buildJsonObject { GoalSourceType.entries.forEach { put(it.name, GoalSources.allowedFor(goal, it)) } })
                        put("canPauseOrDelete", GoalLowering.canPauseOrDelete(goal))
                        put("countedFor", JsonArray(GoalSources.countedFor(goal).map { JsonPrimitive(it.id.toString()) }))
                    }
                }
                JsonParity.difference(case.expected, actual)
            }
        }
    }
}
