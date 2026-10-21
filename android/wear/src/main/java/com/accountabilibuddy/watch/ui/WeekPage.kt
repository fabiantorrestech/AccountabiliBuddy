package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.FOCUS_COLOR
import com.accountabilibuddy.shared.WeekView
import java.time.format.TextStyle as DateTextStyle
import java.util.Locale
import kotlin.math.cos
import kotlin.math.sin

/**
 * The 7 days ending today as small rings around the middle (the oldest at the top, clockwise, today last with a white
 * edge), each filled by focus against the day's planned work. The middle shows the totals.
 */
@Composable
fun WeekPage(view: WeekView) {
    val measurer = rememberTextMeasurer()

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val small = size.minDimension * 0.08f
            val ring = 4.dp.toPx()
            val orbit = size.minDimension / 2 - small - 22.dp.toPx()
            view.days.forEachIndexed { index, day ->
                val radians = Math.toRadians(-90.0 + index * 360.0 / view.days.size)
                val at = Offset(center.x + orbit * cos(radians).toFloat(), center.y + orbit * sin(radians).toFloat())
                val fraction = if (day.plannedMinutes == 0) 0f else day.focusedMinutes.toFloat() / day.plannedMinutes
                drawCircle(Color(0x26FFFFFF), small, at, style = Stroke(ring))
                progressArc(fraction, small, ring, if (fraction >= 1f) Color(0xFF22C55E) else Color(FOCUS_COLOR), at)
                if (day.isToday) {
                    drawCircle(Color.White, small + ring, at, style = Stroke(1.dp.toPx()))
                }

                val letter = day.date.dayOfWeek.getDisplayName(DateTextStyle.NARROW, Locale.getDefault())
                val text = measurer.measure(letter, TextStyle(color = Color.White, fontSize = 10.sp))
                drawText(text, topLeft = Offset(at.x - text.size.width / 2f, at.y - text.size.height / 2f))
            }
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text("Last 7 days", color = Color(0x99FFFFFF), fontSize = 11.sp)
            Text(view.headline, color = Color.White, fontSize = if (view.headline.length > 8) 16.sp else 24.sp, fontWeight = FontWeight.SemiBold)
            Text(view.detail, color = Color(0xB3FFFFFF), fontSize = 10.sp)
        }
    }
}
