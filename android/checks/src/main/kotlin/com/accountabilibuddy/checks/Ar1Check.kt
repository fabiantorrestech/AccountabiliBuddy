package com.accountabilibuddy.checks

/** One AR1-fix fixture file (area `ar1`): each case goes through the phone's twin of the PC's Archive builder ([compare] gives null when they agree). */
class Ar1Check(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "ar1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
