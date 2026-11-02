package com.accountabilibuddy.companion.ui.theme

import androidx.compose.runtime.staticCompositionLocalOf

/** The colours in use. Read through [Ab.colors]. Defaults to Instrument dark so a preview or a stray dialog still draws. */
val LocalAbColors = staticCompositionLocalOf { AbPalettes.InstrumentDark }

/** The corner radii and card edge in use. Read through [Ab.shapes]. */
val LocalAbShapes = staticCompositionLocalOf { AbShapes.of(ThemeName.Instrument) }

/** The fonts and type scale in use. Read through [Ab.type]. */
val LocalAbTypography = staticCompositionLocalOf { AbTypography.of(ThemeName.Instrument) }

/** True while the colour setting is Quiet: colour only for progress and the one main button (the PC's `Ab.IsQuietColour`). */
val LocalQuietColour = staticCompositionLocalOf { true }
