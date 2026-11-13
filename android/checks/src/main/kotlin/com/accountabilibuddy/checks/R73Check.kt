package com.accountabilibuddy.checks

/**
 * One R7.3 fixture file (area `r73`, written by the C# checks' `R73Fixtures`): each case goes through the phone's twin of the PC's
 * rule ([compare] gives null when they agree, else the difference).
 */
class R73Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "r73"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
