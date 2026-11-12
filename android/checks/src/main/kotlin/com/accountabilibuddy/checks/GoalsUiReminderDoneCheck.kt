package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalReminderPlan
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** Round 2 R6b3: "Skip when the goal is already done" ([GoalReminderPlan.isDone]) against the PC's `GoalReminderSource.IsDone`, every snapshot world. */
class GoalsUiReminderDoneCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "reminder-done"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val world = GoalFixtureWorld(case.input)
                val facts = GoalFactsBuilder.build(world.goal, world.data(), world.today, world.weekStart)
                val expected = case.expected.jsonObject.getValue("done").jsonPrimitive.boolean
                val actual = GoalReminderPlan.isDone(facts)
                if (actual == expected) null else "C# $expected, kotlin $actual"
            }
        }
    }
}
