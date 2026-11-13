package com.accountabilibuddy.checks

/**
 * One R7.2 fixture file (area `r72`, written by the C# checks' `R72Fixtures`): each case goes through the phone's twin of the PC's
 * task rule ([compare] gives null when they agree, else the difference).
 */
class R72Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "r72"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
