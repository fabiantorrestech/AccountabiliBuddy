package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalNumbers
import com.accountabilibuddy.shared.goals.GoalTexts
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * Round 2 R6a: a goal row on the phone's Goals list against the PC's (goals-ui/rows.json, written by the out-of-repo GoalsUiFixtures
 * program from the goals snapshot inputs): [GoalFactsBuilder] then [GoalTexts] / [GoalNumbers] give the same target line, kind,
 * number, bar share, visual and pill as the PC's App does from the same synced data.
 */
class GoalsUiRowsCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "rows"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val world = GoalFixtureWorld(case.input)
                val facts = GoalFactsBuilder.build(world.goal, world.data(), world.today, world.weekStart)
                val pill = GoalNumbers.pill(facts)
                val fraction = GoalNumbers.fraction(facts)
                val actual = buildJsonObject {
                    put("targetLine", GoalTexts.targetLine(world.goal, world.today))
                    put("kindText", GoalTexts.kindText(world.goal))
                    put("number", GoalNumbers.number(facts))
                    put("fraction", if (fraction.isNaN()) JsonNull else GoalJson.number(fraction))
                    put("visual", GoalNumbers.visual(facts).name)
                    put("pill", pill?.first?.let(::JsonPrimitive) ?: JsonNull)
                    put("pillAmber", pill?.second ?: false)
                    put("hasPeriodTarget", facts.hasPeriodTarget)
                    put("isPaused", facts.isPaused)
                    put("pausedSince", facts.pausedSince?.toString()?.let(::JsonPrimitive) ?: JsonNull)
                    put("threshold", facts.threshold?.let(GoalJson::number) ?: JsonNull)
                    put("status", facts.status.name)
                }
                JsonParity.difference(case.expected, actual)
            }
            val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
