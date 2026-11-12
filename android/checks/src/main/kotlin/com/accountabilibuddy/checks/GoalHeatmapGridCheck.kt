package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalHeatmapGrid
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDate

/** GoalHeatmapGrid (forRange, rangeStartOf, monthsOf, rowOf, columnOf, dayAt, shows, cropOldest) against the PC, every week start. */
class GoalHeatmapGridCheck : FixtureCheck {
    override val area = "goals"
    override val name = "heatmap-grid"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val range = GoalHeatmapRange.valueOf(case.text("range"))
                val end = LocalDate.parse(case.text("end"))
                val grid = GoalHeatmapGrid.forRange(range, end, GoalWeekStart.valueOf(case.text("weekStart")))
                JsonParity.difference(case.expected, buildJsonObject {
                    put("grid", GoalSnapshotJson.grid(grid))
                    put("rangeStartOf", GoalHeatmapGrid.rangeStartOf(range, end).toString())
                    put("monthsOf", GoalHeatmapGrid.monthsOf(range))
                    put("crop3", GoalSnapshotJson.grid(grid.cropOldest(3)))
                    put("crop500", GoalSnapshotJson.grid(grid.cropOldest(500)))
                    put("cropNegative", GoalSnapshotJson.grid(grid.cropOldest(-2)))
                })
            }
        }
    }
}
