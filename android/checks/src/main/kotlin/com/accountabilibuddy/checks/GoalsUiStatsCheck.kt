package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalHeatCellMark
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalStatsHistory
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * Round 2 R6b4: Stats for one goal on the phone against the PC's (goals-ui/stats.json, written by the out-of-repo GoalsUiFixtures
 * program from the goals snapshot inputs): [GoalStatsHistory] gives `StatsGoalHistoryViewModel`'s subtitle, age, streak and paused
 * stretches ("Paused Sep 14 – Oct 30"), and [GoalHeatmap] over a year gives its heatmap's cells (before the first layout), a paused
 * day hatched ([GoalHeatCellMark.Paused]) where the PC hatches it.
 */
class GoalsUiStatsCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "stats"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        var hatched = 0
        for (case in fixture.cases) {
            val result = runCatching {
                val world = GoalFixtureWorld(case.input)
                val data = world.data()
                val goal = world.goal
                val today = world.today
                val facts = GoalFactsBuilder.build(goal, data, today, world.weekStart)
                val history = GoalStatsHistory.of(facts, "Learning")
                val map = GoalHeatmap.build(goal, data, today, world.weekStart, GoalHeatmapRange.Year, today, GoalHeatmap.UNMEASURED_COLUMNS)
                hatched += map.cells.count { it.mark == GoalHeatCellMark.Paused }
                val actual = buildJsonObject {
                    put("subtitle", history.subtitle)
                    put("ageText", history.ageText)
                    put("streakText", history.streakText)
                    put("pausedStretches", JsonArray(history.pausedStretches.map { JsonPrimitive(it) }))
                    put("pausedNote", GoalStatsHistory.PAUSED_NOTE)
                    put("heatmap", buildJsonObject {
                        put("title", map.title)
                        put("dates", text(map.dates))
                        put("legend", map.legend)
                        put("columns", map.grid.columns)
                        put("cells", JsonArray(map.cells.map { cell ->
                            buildJsonObject {
                                put("row", cell.row)
                                put("column", cell.column)
                                put("level", cell.level)
                                put("tip", cell.tip)
                                put("mark", cell.mark.name)
                                put("progress", GoalJson.number(cell.progress))
                            }
                        }))
                    })
                }
                JsonParity.difference(case.expected, actual)
            }
            val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }

        // The fixture must really hold paused days, or "hatched as on the PC" would pass on nothing.
        report.check("$area/$name: the fixture's worlds hold paused days, hatched on both sides", hatched > 0, "paused cells: $hatched")
    }

    private fun text(value: String?): JsonElement = value?.let { JsonPrimitive(it) } ?: JsonNull
}
