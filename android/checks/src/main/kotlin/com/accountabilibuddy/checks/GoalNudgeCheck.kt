package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalNudge
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** Round 2 R6b3: the phone's [GoalNudge.droppedBehind] against the PC's `GoalNudge.DroppedBehind`. */
class GoalNudgeCheck : FixtureCheck {
    override val area = "goals"
    override val name = "nudge"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val world = GoalFixtureWorld(case.input)
                val expected = case.expected.jsonObject.getValue("dropped").jsonPrimitive.boolean
                val actual = GoalNudge.droppedBehind(world.goal, world.data(), world.today, world.weekStart)
                if (actual == expected) null else "C# $expected, kotlin $actual"
            }
        }
    }
}
