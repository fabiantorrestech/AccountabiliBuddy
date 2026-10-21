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
import com.accountabilibuddy.watch.model.RunningSession
import java.time.Duration
import java.time.LocalDateTime

/**
 * The running session as one ring that fills as it goes, with the time left in the middle. A session with no set
 * end fills once an hour and shows the time so far. Nothing running: a quiet empty ring.
 */
@Composable
fun SessionPage(running: RunningSession?, now: LocalDateTime) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val (fraction, big, small) = when {
            running == null -> Triple(0f, "—", "No focus session")
            running.endsAt == null -> {
                val elapsed = Duration.between(running.startedAt, now).coerceAtLeast(Duration.ZERO)
                Triple((elapsed.toMinutes() % 60) / 60f, timer(elapsed), "since ${clock(running.startedAt.toLocalTime())}")
            }
            else -> {
                val total = Duration.between(running.startedAt, running.endsAt).seconds.coerceAtLeast(1)
                val left = Duration.between(now, running.endsAt).coerceAtLeast(Duration.ZERO)
                Triple(1f - left.seconds.toFloat() / total, timer(left), "ends ${clock(running.endsAt.toLocalTime())}")
            }
        }
        val color = running?.let { Color(it.color) } ?: Color(0x66FFFFFF)

        Canvas(Modifier.fillMaxSize()) {
            val ring = 12.dp.toPx()
            val radius = size.minDimension / 2 - 8.dp.toPx() - ring / 2
            ringTrack(radius, ring, Color(0x26FFFFFF))
            progressArc(fraction, radius, ring, color)
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(running?.profile ?: "Session", color = color, fontSize = 13.sp, fontWeight = FontWeight.Medium)
            Text(big, color = Color.White, fontSize = 34.sp, fontWeight = FontWeight.SemiBold)
            Text(small, color = Color(0xB3FFFFFF), fontSize = 12.sp)
        }
    }
}

/** E.g. "24:59" or "1:05:12". */
private fun timer(duration: Duration): String {
    val hours = duration.toHours()
    val minutes = duration.toMinutesPart()
    val seconds = duration.toSecondsPart()
    return if (hours > 0) "%d:%02d:%02d".format(hours, minutes, seconds) else "%d:%02d".format(minutes, seconds)
}
