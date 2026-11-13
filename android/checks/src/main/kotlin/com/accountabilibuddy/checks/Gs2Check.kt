package com.accountabilibuddy.checks

/**
 * One GS1a / GS1b fixture file (areas `gs1a` and `gs1b`, written by the C# checks' `Gs1aFixtures` / `Gs1bFixtures`): each case goes through the
 * phone's twin of the PC's goals sync rule ([compare] gives null when they agree, else the difference).
 */
class Gs2Check(override val area: String, override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") { compare(case) }
        }
    }
}
