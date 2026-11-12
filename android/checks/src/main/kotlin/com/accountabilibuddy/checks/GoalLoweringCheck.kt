package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalLowering
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Duration
import java.util.UUID

/** GoalLowering.decide against the PC's GoalLowering.Decide (Deep work by flag or id, PB vs AB, raising, other goals). */
class GoalLoweringCheck : FixtureCheck {
    override val area = "goals"
    override val name = "lowering"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val input = case.input
                val from = input.getValue("from").takeIf { it !is JsonNull }?.jsonPrimitive?.doubleOrNull
                val to = input.getValue("to").takeIf { it !is JsonNull }?.jsonPrimitive?.doubleOrNull
                val target = GoalTarget(GoalPeriod.Day, from, GoalUnit.Minutes, GoalDirection.valueOf(case.text("direction")))
                val now = GoalJson.parseTime(case.text("now"))
                val goal = Goal(
                    UUID.fromString(case.text("goalId")), UUID(0, 1), "Goal", "school", "#2E7D6B", GoalKind.Time, target, emptyList(),
                    now, 1.0, now, builtIn = input.getValue("builtIn").jsonPrimitive.boolean,
                )
                val delay = Duration.ofSeconds((input.getValue("delayMinutes").jsonPrimitive.doubleOrNull!! * 60).toLong())
                val decision = GoalLowering.decide(
                    goal, target, to, input.getValue("productaBuddy").jsonPrimitive.boolean, input.getValue("lowerWaitsNow").jsonPrimitive.boolean,
                    now, delay,
                )
                JsonParity.difference(case.expected, buildJsonObject {
                    put("outcome", decision.outcome.name)
                    put("appliesAt", GoalSnapshotJson.time(decision.appliesAt))
                    put("madeInProductaBuddy", decision.madeInProductaBuddy)
                })
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
