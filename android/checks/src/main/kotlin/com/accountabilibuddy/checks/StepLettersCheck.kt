package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.TaskStepLetters
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonPrimitive

/** Round 2 R2 review: the phone's TaskStepLetters.of (Today's "Done · b") against the PC's TaskStepLetters.Of. */
class StepLettersCheck : FixtureCheck {
    override val area = "today"
    override val name = "step-letters"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val index = case.input["index"]!!.jsonPrimitive.int
            val actual = TaskStepLetters.of(index)
            val expected = case.expected.jsonPrimitive.content
            report.check("$area/$name: ${case.name}", actual == expected, "kotlin $actual vs C# $expected")
        }
    }
}
