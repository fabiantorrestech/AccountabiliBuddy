package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalNewerValueException
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * GS0 (ADR 79 forward compatibility): a stored goal list read and written again by the phone ([GoalJson.readItems] /
 * [GoalJson.writeItems], as `PhoneGoalStore` does) against the PC's store (`GoalNewerJson`): unknown fields kept on every object and
 * nested level, items with an unknown enum kept as written at their places, the same JSON and the same text byte for byte; damaged
 * JSON still refused (an [IllegalArgumentException] that is not a [GoalNewerValueException]).
 */
class Gs0RoundTripCheck : FixtureCheck {
    override val area = "gs0"
    override val name = "round-trip"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val kind = case.text("kind")
                val list = case.input.getValue("list")
                val expected = case.expected.jsonObject
                if (expected["damaged"] != null) {
                    val thrown = runCatching { roundTrip(kind, list) }.exceptionOrNull()
                    when {
                        thrown == null -> "the PC refuses it as damaged; the phone read it"
                        thrown is GoalNewerValueException -> "the PC refuses it as damaged; the phone took it for a newer item"
                        thrown !is IllegalArgumentException -> "threw ${thrown::class.simpleName}, not IllegalArgumentException"
                        else -> null
                    }
                } else {
                    val actual = roundTrip(kind, list)
                    JsonParity.difference(expected.getValue("list"), actual)
                        ?: GoalCaseRunner.sameText(expected.getValue("text").jsonPrimitive.content, GoalJson.text(actual))
                }
            }
        }
    }

    private fun roundTrip(kind: String, list: JsonElement): JsonArray {
        val array = list as? JsonArray ?: throw IllegalArgumentException("not a list")
        return when (kind) {
            "areas" -> GoalJson.readItems(array, GoalJson::readArea).let { (known, newer) -> GoalJson.writeAreas(known, newer) }
            "goals" -> GoalJson.readItems(array, GoalJson::readGoal).let { (known, newer) -> GoalJson.writeGoals(known, newer) }
            "entries" -> GoalJson.readItems(array, GoalJson::readEntry).let { (known, newer) -> GoalJson.writeEntries(known, newer) }
            "reminders" -> GoalJson.readItems(array, GoalJson::readReminder).let { (known, newer) -> GoalJson.writeReminders(known, newer) }
            "reviews" -> GoalJson.readItems(array, GoalJson::readReview).let { (known, newer) -> GoalJson.writeReviews(known, newer) }
            else -> error("unknown kind $kind")
        }
    }
}
