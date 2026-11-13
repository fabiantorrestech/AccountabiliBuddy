package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalMonthLineRules
import com.accountabilibuddy.shared.goals.GoalMonthLines
import com.accountabilibuddy.shared.goals.MonthLinePoint
import com.accountabilibuddy.shared.goals.MonthLineToken
import com.accountabilibuddy.shared.goals.MonthStart
import com.accountabilibuddy.shared.tasks.TasksTabletLayout
import java.time.LocalDate

/**
 * R7.5 + HM1 (phone only): the tablet sizes of the phone's Tasks layout (TasksTabletLayout) and the heatmaps' month lines (token per
 * value, the setting's stored name, the staircase and the grids' month starts).
 */
object R75SelfCheck {
    fun run(report: CheckReport) {
        tablet(report)
        monthLines(report)
        staircase(report)
        starts(report)
    }

    private fun tablet(report: CheckReport) {
        val n = "r75/tablet"
        report.check("$n: 600 dp smallest width is a tablet, 599 is not", TasksTabletLayout.isTablet(600) && !TasksTabletLayout.isTablet(599))
        report.check("$n: a phone turned sideways (412 smallest) stays a phone", !TasksTabletLayout.isTablet(412))
        report.check("$n: a phone keeps the whole pane", TasksTabletLayout.contentWidthDp(915, false) == 915 && TasksTabletLayout.contentWidthDp(360, false) == 360)
        report.check("$n: a tablet's content stops at the readable maximum", TasksTabletLayout.contentWidthDp(1280, true) == 720 && TasksTabletLayout.contentWidthDp(1000, true) == 720)
        report.check("$n: a narrow tablet pane (split screen of 600) uses all of it", TasksTabletLayout.contentWidthDp(600, true) == 600 && TasksTabletLayout.contentWidthDp(700, true) == 700)
        report.check("$n: peeks are 24 dp on a phone and larger on a tablet", TasksTabletLayout.peekWidthDp(false) == 24 && TasksTabletLayout.peekWidthDp(true) > 24)
        report.check("$n: the peek's name and marker are a size up on a tablet", TasksTabletLayout.peekNameSp(true) > TasksTabletLayout.peekNameSp(false) && TasksTabletLayout.peekIconDp(true) > TasksTabletLayout.peekIconDp(false))
        val phoneCard = TasksTabletLayout.cardWidthDp(360, false, 4)
        report.check("$n: the phone card is unchanged (360 wide: 360 - 12 - 48 - 16 = 284)", phoneCard == 284)
        val wide = TasksTabletLayout.cardWidthDp(1280, true, 4)
        report.check("$n: the tablet card is wider than the phone's and fits the content", wide > phoneCard && wide + 2 * 6 + 2 * 40 + 2 * 8 == 720)
        report.check("$n: one page has no peeks and a 16 dp side", TasksTabletLayout.cardWidthDp(360, false, 1) == 328 && TasksTabletLayout.cardWidthDp(1280, true, 1) == 688)
        report.check("$n: the card never goes under a readable width at the smallest tablet", TasksTabletLayout.cardWidthDp(600, true, 5) >= 400)
    }

    private fun monthLines(report: CheckReport) {
        val n = "r75/lines"
        report.check("$n: the controls' order is Darker, Normal, Lighter, Off", GoalMonthLines.entries.map { it.label } == listOf("Darker", "Normal", "Lighter", "Off"))
        report.check("$n: the default is Normal", GoalMonthLines.DEFAULT == GoalMonthLines.Normal)
        report.check("$n: Normal is the faint token (the look before)", GoalMonthLines.Normal.token == MonthLineToken.Faint)
        report.check("$n: Lighter is the line token", GoalMonthLines.Lighter.token == MonthLineToken.Line)
        report.check("$n: Darker is the muted token", GoalMonthLines.Darker.token == MonthLineToken.Muted)
        report.check("$n: Off draws no line", GoalMonthLines.Off.token == MonthLineToken.None)
        report.check("$n: every value is read back from its stored name", GoalMonthLines.entries.all { GoalMonthLines.fromName(it.name) == it })
        report.check("$n: nothing stored, or a name this version doesn't know, reads as Normal", GoalMonthLines.fromName(null) == GoalMonthLines.Normal && GoalMonthLines.fromName("Bold") == GoalMonthLines.Normal && GoalMonthLines.fromName("") == GoalMonthLines.Normal)
    }

    private fun staircase(report: CheckReport) {
        val n = "r75/stair"
        // A month whose 1st is on row 3 of column 5: down the right of column 5 to row 3, left across, down to the foot.
        val mid = GoalMonthLineRules.staircase(5, 3)
        report.check("$n: the 1st on row 3 gives a three-segment staircase", mid == listOf(MonthLinePoint(6, 0), MonthLinePoint(6, 3), MonthLinePoint(5, 3), MonthLinePoint(5, 7)), mid.toString())
        val top = GoalMonthLineRules.staircase(5, 0)
        report.check("$n: the 1st on row 0 is one straight line", top == listOf(MonthLinePoint(5, 0), MonthLinePoint(5, 7)), top.toString())
        val last = GoalMonthLineRules.staircase(2, 6)
        report.check("$n: the 1st on the last row steps at row 6", last == listOf(MonthLinePoint(3, 0), MonthLinePoint(3, 6), MonthLinePoint(2, 6), MonthLinePoint(2, 7)), last.toString())
        report.check("$n: no two neighbouring points are the same", listOf(mid, top, last).all { path -> path.zipWithNext().all { (a, b) -> a != b } })
    }

    private fun starts(report: CheckReport) {
        val n = "r75/starts"
        // A grid whose first cell (column 0, row 0) is Monday 2026-10-26: Nov 1 is Sunday = offset 6 (column 0, row 6); Dec 1 is Tuesday = offset 36 (column 5, row 1).
        val first = LocalDate.of(2026, 10, 26)
        val fromFirst = GoalMonthLineRules.monthStarts(0, 0, first, 8)
        report.check("$n: months start where their 1st falls", fromFirst == listOf(MonthStart(0, 6), MonthStart(5, 1)), fromFirst.toString())
        // The same grid found from a cell in the middle (column 3, row 2 = offset 23 = Nov 18).
        val fromMiddle = GoalMonthLineRules.monthStarts(3, 2, LocalDate.of(2026, 11, 18), 8)
        report.check("$n: any known cell gives the same starts", fromMiddle == fromFirst, fromMiddle.toString())
        report.check("$n: a grid starting on the 1st has no line for it (nothing is before it)", GoalMonthLineRules.monthStarts(0, 0, LocalDate.of(2026, 11, 1), 2).isEmpty())
        report.check("$n: a grid of two weeks inside one month has none", GoalMonthLineRules.monthStarts(0, 0, LocalDate.of(2026, 11, 2), 2).isEmpty())
    }
}
