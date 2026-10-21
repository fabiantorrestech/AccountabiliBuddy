package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Text
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
import com.accountabilibuddy.watch.model.DayTotal
import java.time.LocalDate
import java.time.format.TextStyle as DateTextStyle
import java.util.Locale
import kotlin.math.cos
import kotlin.math.sin

/**
 * The week as seven small rings around the middle (Monday at the top, clockwise), each filled by focus against the
 * day's planned work; today has a white edge. The middle shows the week's total.
 */
@Composable
fun WeekPage(week: List<DayTotal>, today: LocalDate) {
    val measurer = rememberTextMeasurer()
    val focused = week.sumOf { it.focusedMinutes }
    val planned = week.filter { !it.date.isAfter(today) }.sumOf { it.plannedMinutes }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val small = size.minDimension * 0.085f
            val ring = 4.dp.toPx()
            val orbit = size.minDimension / 2 - small - 10.dp.toPx()
            week.forEachIndexed { index, day ->
                val radians = Math.toRadians(-90.0 + index * 360.0 / week.size)
                val at = Offset(center.x + orbit * cos(radians).toFloat(), center.y + orbit * sin(radians).toFloat())
                val fraction = if (day.plannedMinutes == 0) 0f else day.focusedMinutes.toFloat() / day.plannedMinutes
                val future = day.date.isAfter(today)
                drawCircle(Color(0x26FFFFFF), small, at, style = Stroke(ring))
                if (!future) {
                    progressArc(fraction, small, ring, if (fraction >= 1f) Color(0xFF22C55E) else Color(0xFF14B8A6), at)
                }
                if (day.date == today) {
                    drawCircle(Color.White, small + ring, at, style = Stroke(1.dp.toPx()))
                }

                val letter = day.date.dayOfWeek.getDisplayName(DateTextStyle.NARROW, Locale.getDefault())
                val text = measurer.measure(letter, TextStyle(color = if (future) Color(0x66FFFFFF) else Color.White, fontSize = 10.sp))
                drawText(text, topLeft = Offset(at.x - text.size.width / 2f, at.y - text.size.height / 2f))
            }
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text("This week", color = Color(0x99FFFFFF), fontSize = 11.sp)
            Text(hoursAndMinutes(focused), color = Color.White, fontSize = 24.sp, fontWeight = FontWeight.SemiBold)
            Text("of ${hoursAndMinutes(planned)} planned so far", color = Color(0xB3FFFFFF), fontSize = 10.sp)
        }
    }
}
