package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.isSystemInDarkTheme
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
fun DayBar(model: DayBarModel, modifier: Modifier = Modifier, compact: Boolean = false) {
    val dark = isSystemInDarkTheme()
    Canvas(modifier.fillMaxWidth().height(DayBarPainter.heightDp(compact).dp)) {
        drawIntoCanvas { canvas ->
            DayBarPainter.draw(canvas.nativeCanvas, model, size.width, density, dark, compact)
        }
    }
}
