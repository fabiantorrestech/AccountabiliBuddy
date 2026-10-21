package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.FOCUS_COLOR
import com.accountabilibuddy.shared.SessionView

/**
 * The running session as one ring that fills as it goes, with the time left in the middle. A session with no set
 * end fills once an hour and shows the time so far. Nothing running: a quiet empty ring.
 */
@Composable
fun SessionPage(view: SessionView) {
    val color = when {
        !view.active -> Color(0x66FFFFFF)
        view.title == "Break" -> Color(0xFF86EFAC)
        else -> Color(FOCUS_COLOR)
    }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val ring = 12.dp.toPx()
            val radius = size.minDimension / 2 - 22.dp.toPx() - ring / 2
            ringTrack(radius, ring, Color(0x26FFFFFF))
            progressArc(view.fraction, radius, ring, color)
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(view.title, color = color, fontSize = 13.sp, fontWeight = FontWeight.Medium)
            Text(view.big, color = Color.White, fontSize = 32.sp, fontWeight = FontWeight.SemiBold)
            Text(view.small, color = Color(0xB3FFFFFF), fontSize = 12.sp)
            view.updated?.let { Text(it, color = Color(0xFFFCD34D), fontSize = 10.sp) }
        }
    }
}
