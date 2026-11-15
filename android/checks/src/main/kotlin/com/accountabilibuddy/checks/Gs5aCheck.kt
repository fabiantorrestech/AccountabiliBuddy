package com.accountabilibuddy.checks

/**
 * One GS5a fixture file (area `gs5a`, written by the C# checks' `Gs5aFixtures`): the task goal link (ADR 76 amended). Until GS5b the phone does
 * not know the field: [compare] proves it keeps `goalId` on the task and `goal` in its stamps through a read and a write, byte for byte
 * (they travel as unknown fields), so a phone's file never clears a link the PC made.
 */
class Gs5aCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "gs5a"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
