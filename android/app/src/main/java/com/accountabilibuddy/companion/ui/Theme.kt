package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/** The PC app's look: dark slate with the focus teal. */
private val Dark = darkColorScheme(
    primary = Color(0xFF5EEAD4),
    onPrimary = Color(0xFF042F2E),
    primaryContainer = Color(0xFF0F766E),
    onPrimaryContainer = Color.White,
    secondary = Color(0xFFA5B4FC),
    background = Color(0xFF101418),
    surface = Color(0xFF101418),
    secondaryContainer = Color(0xFF312E81),
    surfaceContainerLowest = Color(0xFF0B0F12),
    surfaceContainerLow = Color(0xFF13181D),
    surfaceContainer = Color(0xFF161B21),
    surfaceContainerHigh = Color(0xFF1C222A),
    surfaceContainerHighest = Color(0xFF222932),
    onBackground = Color(0xFFE6E8EA),
    onSurface = Color(0xFFE6E8EA),
    onSurfaceVariant = Color(0xFFAAB2BB),
)

private val Light = lightColorScheme(
    primary = Color(0xFF0F766E),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFCCFBF1),
    onPrimaryContainer = Color(0xFF042F2E),
    secondary = Color(0xFF4F46E5),
    secondaryContainer = Color(0xFFE0E7FF),
    background = Color(0xFFF6F8F9),
    surface = Color(0xFFF6F8F9),
    surfaceContainerLowest = Color.White,
    surfaceContainerLow = Color(0xFFF0F3F4),
    surfaceContainer = Color(0xFFEBEFF1),
    surfaceContainerHigh = Color(0xFFE5EAEC),
    surfaceContainerHighest = Color(0xFFDFE5E8),
    onBackground = Color(0xFF15191E),
    onSurface = Color(0xFF15191E),
    onSurfaceVariant = Color(0xFF55606B),
)

@Composable
fun CompanionTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) Dark else Light, content = content)
}
