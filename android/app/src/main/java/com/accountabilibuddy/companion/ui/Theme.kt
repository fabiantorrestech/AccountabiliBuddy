package com.accountabilibuddy.companion.ui

import android.app.Activity
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import com.accountabilibuddy.companion.ui.theme.AbColors
import com.accountabilibuddy.companion.ui.theme.AbPalettes
import com.accountabilibuddy.companion.ui.theme.AbShapes
import com.accountabilibuddy.companion.ui.theme.AbTypography
import com.accountabilibuddy.companion.ui.theme.ColourMode
import com.accountabilibuddy.companion.ui.theme.LocalAbColors
import com.accountabilibuddy.companion.ui.theme.LocalAbShapes
import com.accountabilibuddy.companion.ui.theme.LocalAbTypography
import com.accountabilibuddy.companion.ui.theme.LocalQuietColour
import com.accountabilibuddy.companion.ui.theme.ThemeName
import com.accountabilibuddy.companion.ui.theme.rememberDarkNow

/**
 * The app's look: the theme (Instrument, Night Desk or Paper), light or dark (as the user set it in Settings → Appearance,
 * by the clock or by sunset), and the animation speed. The tokens go out as [LocalAbColors], [LocalAbShapes] and
 * [LocalAbTypography], and are also mapped onto the Material 3 colour scheme, so screens not redone yet and stock Material
 * widgets follow the theme too (ADR 49).
 */
@Composable
fun CompanionTheme(systemBarIconsFollowTheme: Boolean = true, content: @Composable () -> Unit) {
    val themeName by UiPrefs.themeName.collectAsState()
    val mode by UiPrefs.mode.collectAsState()
    val colour by UiPrefs.colour.collectAsState()
    val darkAt by UiPrefs.darkAt.collectAsState()
    val lightAt by UiPrefs.lightAt.collectAsState()
    val place by UiPrefs.place.collectAsState()
    val motion by UiPrefs.motion.collectAsState()

    val dark = rememberDarkNow(mode, darkAt, lightAt, place)
    ThemedContent(themeName, dark, colour == ColourMode.Quiet, motion.scale, systemBarIconsFollowTheme, content)
}

/**
 * The theme for an explicit [themeName] and light or dark: what [CompanionTheme] resolves to, and what the Appearance
 * previews and the debug gallery use to show a theme other than the one in use.
 */
@Composable
fun ThemedContent(
    themeName: ThemeName,
    dark: Boolean,
    quiet: Boolean,
    motionScale: Float,
    systemBarIconsFollowTheme: Boolean = true,
    content: @Composable () -> Unit,
) {
    val colors = AbPalettes.of(themeName, dark)
    val shapes = remember(themeName) { AbShapes.of(themeName) }
    val type = remember(themeName) { AbTypography.of(themeName) }

    // The status and navigation bar icons follow the app's light or dark, not the phone's. The takeover screens that paint
    // their own dark ground pass false and always get light icons, whatever the theme is.
    val view = LocalView.current
    if (!view.isInEditMode) {
        val darkGround = if (systemBarIconsFollowTheme) dark else true
        SideEffect {
            val window = (view.context as? Activity)?.window ?: return@SideEffect
            val controller = WindowCompat.getInsetsController(window, view)
            // "Appearance light" means dark icons (for a light ground); a dark ground wants light icons.
            controller.isAppearanceLightStatusBars = !darkGround
            controller.isAppearanceLightNavigationBars = !darkGround
            // The window behind Compose follows the theme too (the manifest theme only knows the phone's night setting), so a
            // rotation or a recreate does not show the other ground for a frame.
            if (systemBarIconsFollowTheme) window.setBackgroundDrawable(android.graphics.drawable.ColorDrawable(colors.bg.toArgb()))
        }
    }

    CompositionLocalProvider(
        LocalMotionScale provides motionScale,
        LocalAbColors provides colors,
        LocalAbShapes provides shapes,
        LocalAbTypography provides type,
        LocalQuietColour provides quiet,
    ) {
        MaterialTheme(
            colorScheme = remember(colors) { colors.toColorScheme() },
            typography = type.material(),
            shapes = shapes.material(),
            content = content,
        )
    }
}

/**
 * The tokens on the Material 3 scheme. Screens not redone yet draw cards on `surfaceContainerLow` over `surface`, so those
 * two are the theme's surface and background; links and icons that use `primary` get the accent.
 */
private fun AbColors.toColorScheme(): ColorScheme {
    val base = if (isDark) darkColorScheme() else lightColorScheme()
    return base.copy(
        primary = accent,
        onPrimary = onAccent,
        primaryContainer = accentSoft,
        onPrimaryContainer = ink,
        inversePrimary = accent,
        secondary = muted,
        onSecondary = bg,
        secondaryContainer = surface2,
        onSecondaryContainer = ink,
        tertiary = sleep,
        onTertiary = bg,
        tertiaryContainer = infoBg,
        onTertiaryContainer = infoInk,
        background = bg,
        onBackground = ink,
        surface = bg,
        onSurface = ink,
        surfaceVariant = surface2,
        onSurfaceVariant = muted,
        surfaceTint = Color.Transparent,
        inverseSurface = ink,
        inverseOnSurface = bg,
        error = drift,
        onError = bg,
        errorContainer = surface2,
        onErrorContainer = drift,
        outline = faint,
        outlineVariant = line,
        scrim = Color.Black,
        surfaceBright = surface2,
        surfaceDim = bg,
        surfaceContainerLowest = bg,
        surfaceContainerLow = surface,
        surfaceContainer = surface,
        surfaceContainerHigh = surface2,
        surfaceContainerHighest = surface2,
    )
}

private fun AbShapes.material(): Shapes = Shapes(
    small = RoundedCornerShape(small),
    medium = RoundedCornerShape(medium),
    large = RoundedCornerShape(large),
)
