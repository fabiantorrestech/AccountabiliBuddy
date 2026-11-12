package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAppMatchGuess
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive

/** Screen-time source v2 (ST1): the phone's GoalAppMatchGuess.guess against the PC's GoalAppMatchGuess.Guess. */
class ScreenTimeGuessCheck : FixtureCheck {
    override val area = "screentime"
    override val name = "guess"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val input = case.input["text"]!!
            val text = if (input is JsonNull) null else input.jsonPrimitive.content
            val actual = GoalAppMatchGuess.guess(text)
            val expected = case.expected
            val ok = if (expected is JsonNull) {
                actual == null
            } else {
                val obj = expected as JsonObject
                actual != null && actual.kind.name == obj.getValue("kind").jsonPrimitive.content && actual.text == obj.getValue("text").jsonPrimitive.content
            }
            report.check("$area/$name: ${case.name}", ok, "kotlin $actual vs C# $expected")
        }
    }
}
