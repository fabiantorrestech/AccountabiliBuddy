package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.DayBarModel

/** The day bar on screen, drawn by the same painter as the widget and the notification. */
@Composable
fun DayBar(model: DayBarModel, modifier: Modifier = Modifier, compact: Boolean = false, onEvent: ((com.accountabilibuddy.companion.model.CalendarEvent) -> Unit)? = null) {
    val dark = com.accountabilibuddy.companion.ui.theme.Ab.colors.isDark
    val tap = if (onEvent == null) {
        Modifier
    } else {
        // A tap anywhere on the bar's height picks the event at that time (ADR 64); a timed event over an all-day sliver, as it is drawn.
        Modifier.pointerInput(model) {
            detectTapGestures { offset ->
                val at = offset.x.toDouble() / size.width
                model.events.lastOrNull { it.event != null && at >= it.start && at <= it.end }?.event?.let(onEvent)
            }
        }
    }
    Canvas(modifier.fillMaxWidth().height(DayBarPainter.heightDp(compact).dp).then(tap)) {
        drawIntoCanvas { canvas ->
            DayBarPainter.draw(canvas.nativeCanvas, model, size.width, density, dark, compact)
        }
    }
}
