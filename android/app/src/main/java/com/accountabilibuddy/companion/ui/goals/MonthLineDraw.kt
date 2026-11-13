package com.accountabilibuddy.companion.ui.goals

import androidx.compose.runtime.Composable
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalMonthLineRules
import com.accountabilibuddy.shared.goals.GoalMonthLines
import com.accountabilibuddy.shared.goals.MonthLineToken

/** The theme colour for a month line (HM1): Normal = faint, Lighter = line, Darker = muted; null for Off (no line). */
@Composable
fun GoalMonthLines.lineColor(): Color? = when (token) {
    MonthLineToken.Faint -> Ab.colors.faint
    MonthLineToken.Line -> Ab.colors.line
    MonthLineToken.Muted -> Ab.colors.muted
    MonthLineToken.None -> null
}

/**
 * One month's staircase divider (HM1) in the gap between the cells: [column] and [row] are where the month's first day sits; [origin]
 * is the top-left of cell (0, 0), [step] and [rowStep] the cell plus gap. Nothing is drawn with a null [color] (Off), so labels and
 * spacing stay as they are.
 */
fun DrawScope.drawMonthStaircase(color: Color?, column: Int, row: Int, origin: Offset, step: Float, rowStep: Float, gap: Float, width: Float) {
    if (color == null) return
    val points = GoalMonthLineRules.staircase(column, row)
    val path = Path()
    points.forEachIndexed { index, point ->
        val x = origin.x + point.column * step - gap / 2
        val y = origin.y + point.row * rowStep - gap / 2
        if (index == 0) path.moveTo(x, y) else path.lineTo(x, y)
    }
    drawPath(path, color, style = Stroke(width = width, join = StrokeJoin.Miter))
}
