package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalHeatmapGrid
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate

/** GoalHeatmapGrid.step (‹ and ›, never past today) against the PC. */
class GoalHeatmapStepCheck : FixtureCheck {
    override val area = "goals"
    override val name = "heatmap-step"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val day = GoalHeatmapGrid.step(
                    GoalHeatmapRange.valueOf(case.text("range")), LocalDate.parse(case.text("end")), case.input.getValue("direction").jsonPrimitive.int,
                    LocalDate.parse(case.text("today")),
                )
                JsonParity.difference(case.expected, buildJsonObject { put("day", day.toString()) })
            }
        }
    }
}
