package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.TaskPlannedFor
import kotlinx.serialization.json.jsonPrimitive

/** The pipeline's proof (R1.0): TaskPlannedFor.tieText against the PC's PlannedSlot.TieText. */
class PlannedTieTextCheck : FixtureCheck {
    override val area = "proof"
    override val name = "planned-tie-text"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val actual = TaskPlannedFor(case.text("date"), case.text("partId"), case.text("start")).tieText
            val expected = case.expected.jsonPrimitive.content
            report.check("$area/$name: ${case.name}", actual == expected, "kotlin ${visible(actual)} vs C# ${visible(expected)}")
        }
    }

    // The separators are control characters; shown as "|" so a failure line can be read.
    private fun visible(text: String) = text.replace('\u0003', '|')
}
