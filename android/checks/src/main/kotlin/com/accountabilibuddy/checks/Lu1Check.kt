package com.accountabilibuddy.checks

/** One LU1-fix fixture file (area `lu1`): each case goes through the phone's twin of the PC's rule ([compare] gives null when they agree). */
class Lu1Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "lu1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
