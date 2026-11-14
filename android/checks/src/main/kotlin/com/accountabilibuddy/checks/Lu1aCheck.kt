package com.accountabilibuddy.checks

/** One LU1a fixture file (area `lu1a`): each case goes through the phone's twin of the PC's rule ([compare] gives null when they agree). */
class Lu1aCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "lu1a"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
