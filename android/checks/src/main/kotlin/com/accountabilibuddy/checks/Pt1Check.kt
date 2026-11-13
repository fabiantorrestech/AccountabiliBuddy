package com.accountabilibuddy.checks

/** One PT1 fixture file (area `pt1`): each case goes through the phone's twin of the PC's rule ([compare] gives null when they agree). */
class Pt1Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "pt1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
