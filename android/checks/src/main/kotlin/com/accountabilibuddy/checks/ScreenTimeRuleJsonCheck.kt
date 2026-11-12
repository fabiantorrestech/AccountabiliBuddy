package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson

/** Screen-time source v2 (ST1): a source rule's JSON read then written by the phone against the PC's (old `app` text read, apps / devices kept, key order). */
class ScreenTimeRuleJsonCheck : FixtureCheck {
    override val area = "screentime"
    override val name = "rule-json"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val actual = GoalJson.writeSourceRule(GoalJson.readSourceRule(case.input.getValue("rule")))
                JsonParity.difference(case.expected, actual)
                    ?: GoalCaseRunner.sameText(GoalJson.text(case.expected), GoalJson.text(actual))
            }
        }
    }
}
