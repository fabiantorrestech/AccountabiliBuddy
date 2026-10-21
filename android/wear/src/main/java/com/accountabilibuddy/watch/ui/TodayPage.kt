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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.watch.model.WatchSnapshot
import java.time.LocalDateTime
import java.time.LocalTime

/**
 * Today on a 24-hour dial (midnight at the top): the outer ring is the day plan in its category colours, the inner
 * ring the focus sessions (teal, the running one brighter, drifts as red ticks), and a red dot is now. The middle says
 * how much was focused against what was planned, and what's running.
 */
@Composable
fun TodayPage(snapshot: WatchSnapshot, now: LocalDateTime) {
    val running = snapshot.running
    val runningMinutes = running?.let { java.time.Duration.between(it.startedAt, now).toMinutes().toInt().coerceAtLeast(0) } ?: 0

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val ring = 9.dp.toPx()
            val outer = size.minDimension / 2 - 6.dp.toPx() - ring / 2
            val inner = outer - ring - 4.dp.toPx()

            ringTrack(outer, ring, Color(0x26FFFFFF))
            snapshot.plan.forEach { timeArc(it.start, it.end, outer, ring, Color(it.color)) }

            ringTrack(inner, ring, Color(0x1AFFFFFF))
            snapshot.focus.forEach { timeArc(it.start, it.end, inner, ring, Color(it.color).copy(alpha = 0.75f)) }
            running?.let { timeArc(it.startedAt.toLocalTime(), now.toLocalTime(), inner, ring, Color(it.color)) }
            snapshot.drifts.forEach { drift ->
                val minute = minuteOf(drift)
                drawLine(Color(0xFFEF4444), pointAt(minute, inner - ring / 2), pointAt(minute, inner + ring / 2), strokeWidth = 2.dp.toPx())
            }

            // Hour marks inside the rings: midnight, 6, noon, 18.
            for (hour in listOf(0, 6, 12, 18)) {
                drawCircle(Color(0x66FFFFFF), 1.5.dp.toPx(), pointAt(hour * 60, inner - ring - 2.dp.toPx()))
            }

            // Now: a red dot on the outer ring and a hand across both rings.
            val nowMinute = minuteOf(now.toLocalTime())
            drawLine(Color(0xCCEF4444), pointAt(nowMinute, inner - ring / 2), pointAt(nowMinute, outer + ring / 2), strokeWidth = 2.dp.toPx())
            drawCircle(Color(0xFFEF4444), 4.dp.toPx(), pointAt(nowMinute, outer))
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text("Today", color = Color(0x99FFFFFF), fontSize = 11.sp)
            Text(hoursAndMinutes(snapshot.focusedMinutes + runningMinutes), color = Color.White, fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
            Text("of ${hoursAndMinutes(snapshot.plannedMinutes)} planned", color = Color(0xB3FFFFFF), fontSize = 11.sp)
            if (running != null) {
                val left = running.endsAt?.let { java.time.Duration.between(now, it).toMinutes().coerceAtLeast(0) }
                Text(
                    if (left == null) "${running.profile} · running" else "${running.profile} · ${left}m left",
                    color = Color(running.color), fontSize = 12.sp, fontWeight = FontWeight.Medium,
                )
            } else {
                nextPlanned(snapshot, now.toLocalTime())?.let { Text(it, color = Color(0xB3FFFFFF), fontSize = 11.sp) }
            }
        }
    }
}

/** E.g. "Next: Work at 1:00 PM", from the plan's parts later today. */
private fun nextPlanned(snapshot: WatchSnapshot, now: LocalTime): String? =
    snapshot.plan.filter { it.start.isAfter(now) && it.name != "Sleep" }.minByOrNull { it.start }?.let { "Next: ${it.name} at ${clock(it.start)}" }

/** E.g. "2h 55m", "45m", "7h". */
fun hoursAndMinutes(minutes: Int): String {
    val hours = minutes / 60
    val rest = minutes % 60
    return when {
        hours == 0 -> "${rest}m"
        rest == 0 -> "${hours}h"
        else -> "${hours}h ${rest}m"
    }
}
