package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalHeatmapFit
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate

/** GoalHeatmapFit.fit (crop the oldest weeks, step the range down past about a month, never below one week) against the PC. */
class GoalHeatmapFitCheck : FixtureCheck {
    override val area = "goals"
    override val name = "heatmap-fit"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val wanted = GoalHeatmapRange.valueOf(case.text("range"))
                val end = LocalDate.parse(case.text("end"))
                val weekStart = GoalWeekStart.valueOf(case.text("weekStart"))
                val results = case.input.getValue("widths").jsonArray.map { width ->
                    val columns = width.jsonPrimitive.int
                    val fit = GoalHeatmapFit.fit(wanted, end, weekStart, columns)
                    buildJsonObject {
                        put("columnsThatFit", columns)
                        put("range", fit.range.name)
                        put("croppedColumns", fit.croppedColumns)
                        put("steppedDown", fit.steppedDown)
                        put("grid", GoalSnapshotJson.grid(fit.grid))
                    }
                }
                JsonParity.difference(case.expected, JsonArray(results))
            }
        }
    }
}
