package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalNewerLists
import com.accountabilibuddy.shared.goals.GoalUnknowns
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * GS0 (ADR 79): what this version doesn't know comes back after a change rebuilt items ([GoalUnknowns.keep], run by the phone's
 * store through `GoalListRules.tidy`) against the PC's `GoalUnknowns.Keep` / `KeepNewer` (run by `GoalStore.Normalize`).
 */
class Gs0KeepCheck : FixtureCheck {
    override val area = "gs0"
    override val name = "keep"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val kind = case.text("kind")
                val before = lists(kind, case.input.getValue("before") as JsonArray)
                val after = lists(kind, case.input.getValue("after") as JsonArray)
                val actual = write(kind, GoalUnknowns.keep(before, after))
                val expected = case.expected.jsonObject
                JsonParity.difference(expected.getValue("list"), actual)
                    ?: GoalCaseRunner.sameText(expected.getValue("text").jsonPrimitive.content, GoalJson.text(actual))
            }
        }
    }

    private fun lists(kind: String, array: JsonArray): GoalLists = when (kind) {
        "areas" -> GoalJson.readItems(array, GoalJson::readArea).let { (known, newer) -> GoalLists(areas = known, newer = GoalNewerLists(areas = newer)) }
        "goals" -> GoalJson.readItems(array, GoalJson::readGoal).let { (known, newer) -> GoalLists(goals = known, newer = GoalNewerLists(goals = newer)) }
        "entries" -> GoalJson.readItems(array, GoalJson::readEntry).let { (known, newer) -> GoalLists(entries = known, newer = GoalNewerLists(entries = newer)) }
        "reminders" -> GoalJson.readItems(array, GoalJson::readReminder).let { (known, newer) -> GoalLists(reminders = known, newer = GoalNewerLists(reminders = newer)) }
        "reviews" -> GoalJson.readItems(array, GoalJson::readReview).let { (known, newer) -> GoalLists(reviews = known, newer = GoalNewerLists(reviews = newer)) }
        else -> error("unknown kind $kind")
    }

    private fun write(kind: String, lists: GoalLists): JsonArray = when (kind) {
        "areas" -> GoalJson.writeAreas(lists.areas, lists.newer.areas)
        "goals" -> GoalJson.writeGoals(lists.goals, lists.newer.goals)
        "entries" -> GoalJson.writeEntries(lists.entries, lists.newer.entries)
        "reminders" -> GoalJson.writeReminders(lists.reminders, lists.newer.reminders)
        "reviews" -> GoalJson.writeReviews(lists.reviews, lists.newer.reviews)
        else -> error("unknown kind $kind")
    }
}
