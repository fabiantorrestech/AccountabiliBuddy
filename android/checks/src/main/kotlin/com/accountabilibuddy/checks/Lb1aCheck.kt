package com.accountabilibuddy.checks

/** One LB1a fixture file (area `lb1`): each case goes through the phone's twin of the PC's device-only label and tag rule ([compare] gives null when they agree). */
class Lb1aCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "lb1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
