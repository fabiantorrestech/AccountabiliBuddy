package com.accountabilibuddy.companion.ui

import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ProgressIndicatorDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp

/**
 * A "working on it" ring: it spins, or with animations off (Settings › Appearance) it stands still three-quarters full, so an
 * e-ink screen doesn't redraw for it. The words beside it say what is happening.
 */
@Composable
fun BusyRing(
    modifier: Modifier = Modifier,
    color: Color = ProgressIndicatorDefaults.circularColor,
    trackColor: Color = Color.Transparent,
    strokeWidth: Dp = ProgressIndicatorDefaults.CircularStrokeWidth,
) {
    if (LocalMotionScale.current <= 0f) {
        CircularProgressIndicator(progress = { 0.75f }, modifier = modifier, color = color, trackColor = trackColor, strokeWidth = strokeWidth)
    } else {
        CircularProgressIndicator(modifier = modifier, color = color, trackColor = trackColor, strokeWidth = strokeWidth)
    }
}
