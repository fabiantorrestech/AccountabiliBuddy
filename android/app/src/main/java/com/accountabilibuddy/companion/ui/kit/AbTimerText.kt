package com.accountabilibuddy.companion.ui.kit

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A timer or count in the theme's numeral font with tabular numerals, so the digits don't shift as they change
 * (IBM Plex Mono in Instrument, Manrope in Night Desk, Newsreader in Paper). Pick the size from `Ab.type`: `timer`,
 * `timerHuge`, `count` or `timerSmall`.
 */
@Composable
fun AbTimerText(text: String, modifier: Modifier = Modifier, style: TextStyle = Ab.type.timer, color: Color = Ab.colors.ink) {
    Text(text, modifier = modifier, style = style, color = color, maxLines = 1)
}
