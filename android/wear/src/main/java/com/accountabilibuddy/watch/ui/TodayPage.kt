package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.TileRing
import com.accountabilibuddy.shared.TodayView

/**
 * Today on a 24-hour dial (midnight at the top): the outer ring is the day plan in its category colours, the inner
 * ring the focus sessions (teal, the running one brighter, drifts as red ticks), and a red hand is now. The middle says
 * how much was focused against what was planned, what's running or next, and how old the data is.
 */
@Composable
fun TodayPage(view: TodayView) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val ring = 9.dp.toPx()
            // Inside the time at the top of the screen.
            val outer = size.minDimension / 2 - 20.dp.toPx() - ring / 2
            val inner = outer - ring - 4.dp.toPx()

            ringTrack(outer, ring, Color(0x26FFFFFF))
            view.plan.forEach { timeArc(it.startMinute, it.endMinute, outer, ring, Color(it.color)) }

            ringTrack(inner, ring, Color(0x1AFFFFFF))
            view.focus.forEach { arc ->
                timeArc(arc.startMinute, arc.endMinute, inner, ring, Color(arc.color).copy(alpha = if (arc.bright) 1f else 0.7f))
            }
            view.driftMinutes.forEach { minute ->
                drawLine(Color(0xFFEF4444), pointAt(minute, inner - ring / 2), pointAt(minute, inner + ring / 2), strokeWidth = 2.dp.toPx())
            }

            // Clock ticks inside the rings: each hour, longer and brighter at 12, 3, 6 and 9 (AM and PM).
            val tickFrom = inner - ring / 2 - 2.dp.toPx()
            CLOCK_TICKS.forEach { tick ->
                val length = (if (tick.major) 6 else 3).dp.toPx()
                drawLine(
                    if (tick.major) Color(0xCCFFFFFF) else Color(0x66FFFFFF), pointAt(tick.minute, tickFrom), pointAt(tick.minute, tickFrom - length),
                    strokeWidth = (if (tick.major) 2f else 1f).dp.toPx(),
                )
            }

            // Now: a hand across both rings and a red dot on the outer one.
            drawLine(Color(0xCCEF4444), pointAt(view.nowMinute, inner - ring / 2), pointAt(view.nowMinute, outer + ring / 2), strokeWidth = 2.dp.toPx())
            drawCircle(Color(0xFFEF4444), 4.dp.toPx(), pointAt(view.nowMinute, outer))
        }

        Column(
            Modifier.padding(horizontal = 44.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            Text("Today", color = Color(0x99FFFFFF), fontSize = 11.sp)
            Text(view.headline, color = Color.White, fontSize = if (view.headline.length > 8) 17.sp else 26.sp,
                fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center)
            if (view.detail.isNotEmpty()) {
                Text(view.detail, color = Color(0xB3FFFFFF), fontSize = 11.sp, textAlign = TextAlign.Center)
            }
            view.status?.let { Text(it, color = Color(0xFF5EEAD4), fontSize = 12.sp, fontWeight = FontWeight.Medium, textAlign = TextAlign.Center) }
            view.updated?.let { Text(it, color = Color(0xFFFCD34D), fontSize = 10.sp, textAlign = TextAlign.Center) }
        }
    }
}

/** The hour ticks never change, so they are made once, not on every draw. */
private val CLOCK_TICKS = TileRing.ticks()
