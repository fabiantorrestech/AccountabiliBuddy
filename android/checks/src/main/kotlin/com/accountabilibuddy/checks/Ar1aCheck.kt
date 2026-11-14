package com.accountabilibuddy.checks

/** One AR1a fixture file (area `ar1a`): each case goes through the phone's twin of the PC's session note rules ([compare] gives null when they agree). */
class Ar1aCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "ar1a"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
