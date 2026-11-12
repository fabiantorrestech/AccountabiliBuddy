package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalWeekStart
import com.accountabilibuddy.shared.goals.GoalWeekSummary
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.put
import java.time.LocalDate

/**
 * Round 2 R6a: the phone's Goals subtitle counts ("5 of 8 on track") against the PC's This week card (goals-ui/week.json:
 * `GoalThisWeekViewModel.Update` over every goal of one goals snapshot file): [GoalWeekSummary.of] over the same goals' facts.
 */
class GoalsUiWeekCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "week"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val today = LocalDate.parse(case.text("today"))
                val weekStart = GoalWeekStart.valueOf(case.text("weekStart"))
                val facts = case.input.getValue("goals").jsonArray.map { input ->
                    val world = GoalFixtureWorld(input as JsonObject)
                    GoalFactsBuilder.build(world.goal, world.data(), world.today, world.weekStart)
                }
                val summary = GoalWeekSummary.of(facts, today, weekStart)
                val actual = buildJsonObject {
                    put("onTrack", summary.onTrack)
                    put("total", summary.total)
                    put("behind", summary.behind)
                    put("notDue", summary.notDue)
                    put("weekStart", summary.weekStart.toString())
                }
                JsonParity.difference(case.expected, actual)
            }
            val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
