package com.accountabilibuddy.checks

/**
 * One GS5a fixture file (area `gs5a`, written by the C# checks' `Gs5aFixtures` and `Gs5aGoalsChecks`): the task goal link (ADR 76 amended).
 * Since GS5b `goalId` is a real field on the phone's task and `goal` a real stamp: [compare] runs each case through the phone's twin of the
 * PC's rule (task JSON, the field merge, the goal sources' counting, the links read off the task list).
 */
class Gs5aCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "gs5a"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
