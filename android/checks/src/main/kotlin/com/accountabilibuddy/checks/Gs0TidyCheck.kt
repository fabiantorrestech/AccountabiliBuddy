package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAreaTidy
import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.jsonObject

/**
 * GS0: where a live goal in a dead area goes ([GoalAreaTidy.rehome], run by the phone's `GoalListRules.tidy`) against the PC's
 * `GoalAreaTidy.Rehome` (run by `GoalStore.Normalize`): "Work", made again if needed, with stamps from the lists, never the clock.
 * Before GS0 the phone sent such a goal to the first live area without a new stamp.
 */
class Gs0TidyCheck : FixtureCheck {
    override val area = "gs0"
    override val name = "tidy"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val (areas, goals) = GoalAreaTidy.rehome(
                    GoalJson.readAreas(case.input.getValue("areas")),
                    GoalJson.readGoals(case.input.getValue("goals")),
                    GoalJson.readArea(case.input.getValue("work")),
                )
                val expected = case.expected.jsonObject
                JsonParity.difference(expected.getValue("areas"), GoalJson.writeAreas(areas), "$.areas")
                    ?: JsonParity.difference(expected.getValue("goals"), GoalJson.writeGoals(goals), "$.goals")
            }
        }
    }
}
