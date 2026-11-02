package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A progress ring: the accent over a surface2 track, starting at the top. [progress] is 0 to 1. Progress is one of the two
 * places accent is allowed. The size is a parameter (not a fixed layout), so a pane can scale it.
 */
@Composable
fun AbRing(progress: Float, modifier: Modifier = Modifier, diameter: Dp = 56.dp, strokeWidth: Dp = 8.dp) {
    val colors = Ab.colors
    Canvas(modifier.size(diameter)) {
        val stroke = strokeWidth.toPx()
        val inset = stroke / 2
        val arc = Size(size.width - stroke, size.height - stroke)
        drawArc(colors.surface2, 0f, 360f, useCenter = false, topLeft = Offset(inset, inset), size = arc, style = Stroke(stroke))
        val sweep = 360f * progress.coerceIn(0f, 1f)
        if (sweep > 0f) {
            drawArc(colors.accent, -90f, sweep, useCenter = false, topLeft = Offset(inset, inset), size = arc, style = Stroke(stroke, cap = StrokeCap.Round))
        }
    }
}
