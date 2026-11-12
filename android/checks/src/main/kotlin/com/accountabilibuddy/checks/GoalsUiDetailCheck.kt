package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDayPanel
import com.accountabilibuddy.shared.goals.GoalDayPanels
import com.accountabilibuddy.shared.goals.GoalExtraTiles
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalSourceLines
import com.accountabilibuddy.shared.goals.GoalTakesText
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * Round 2 R6b1: a goal's page on the phone against the PC's detail pane (goals-ui/detail.json, written by the out-of-repo
 * GoalsUiFixtures program from the goals snapshot inputs): [GoalTiles] (Streak and Total on), [GoalDayPanels] for today and five days
 * back, [GoalSourceLines], [GoalTakesText] and [GoalHeatmap] (3 months, before the first layout) give what `GoalDetailViewModel.Show` shows.
 */
class GoalsUiDetailCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "detail"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val world = GoalFixtureWorld(case.input)
                val data = world.data()
                val goal = world.goal
                val today = world.today
                val facts = GoalFactsBuilder.build(goal, data, today, world.weekStart)
                val label: (java.util.UUID) -> String? = { "Spanish" }
                val tiles = com.accountabilibuddy.shared.goals.GoalTiles.build(facts, data, GoalExtraTiles(streak = true, total = true))
                val actual = buildJsonObject {
                    put("tiles", JsonArray(tiles.map { tile ->
                        buildJsonObject {
                            put("kind", tile.kind)
                            put("big", tile.big)
                            put("of", text(tile.of))
                            put("caption", tile.caption)
                            put("foot", text(tile.foot))
                            put("showsBar", tile.showsBar)
                            put("fraction", number(tile.fraction))
                            put("mark", number(tile.mark))
                            put("status", text(tile.status))
                            put("statusAmber", tile.statusAmber)
                            put("segments", tile.segments?.size?.let { JsonPrimitive(it) } ?: JsonNull)
                        }
                    }))
                    put("todayPanel", panel(GoalDayPanels.of(goal, GoalCounting.day(goal, data, today, today), today, label)))
                    val past = today.minusDays(5)
                    put("pastPanel", panel(GoalDayPanels.of(goal, GoalCounting.day(goal, data, past, today), today, label)))
                    put("lines", JsonArray(GoalSourceLines.of(goal, facts.threshold, label).map { line ->
                        buildJsonObject {
                            put("text", line.text)
                            put("soon", line.comingSoon)
                        }
                    }))
                    val takes = facts.whatItTakes?.let { GoalTakesText.of(it, goal, today) }
                    put("takes", if (takes == null) JsonNull else buildJsonObject {
                        put("remaining", takes.remaining)
                        put("each", text(takes.each))
                        put("pace", text(takes.pace))
                        put("verdict", takes.verdict)
                        put("green", takes.green)
                        put("amber", takes.amber)
                    })
                    val map = GoalHeatmap.build(goal, data, today, world.weekStart, GoalHeatmapRange.ThreeMonths, today, GoalHeatmap.UNMEASURED_COLUMNS)
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
    }

    private fun text(value: String?): JsonElement = value?.let { JsonPrimitive(it) } ?: JsonNull

    private fun number(value: Double?): JsonElement = value?.let { GoalJson.number(it) } ?: JsonNull

    private fun panel(panel: GoalDayPanel) = buildJsonObject {
        put("day", panel.day.toString())
        put("title", panel.title)
        put("big", panel.big)
        put("of", text(panel.of))
        put("status", panel.status)
        put("teal", panel.tone == GoalDayPanel.Tone.Teal)
        put("amber", panel.tone == GoalDayPanel.Tone.Amber)
        put("grey", panel.tone == GoalDayPanel.Tone.Grey)
        put("source", panel.source)
        put("note", text(panel.note))
        put("isPast", panel.isPast)
        put("canTick", panel.canTick)
        put("tickText", panel.tickText)
        put("canEdit", panel.canEdit)
        put("editCaption", panel.editCaption)
    }
}
