package com.accountabilibuddy.companion.ui.theme

import androidx.compose.runtime.Composable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.ui.graphics.Color

/** Short reads of the theme in use: `Ab.colors.ink`, `Ab.shapes.cardShape`, `Ab.type.title`. */
object Ab {
    val colors: AbColors
        @Composable @ReadOnlyComposable get() = LocalAbColors.current

    val shapes: AbShapes
        @Composable @ReadOnlyComposable get() = LocalAbShapes.current

    val type: AbTypography
        @Composable @ReadOnlyComposable get() = LocalAbTypography.current

    val quiet: Boolean
        @Composable @ReadOnlyComposable get() = LocalQuietColour.current

    /**
     * The colour for a status icon or small mark that has a meaning (met, waiting, drift): that colour in Standard, muted
     * in Quiet (the PC does the same with its `quiet` class).
     */
    @Composable
    @ReadOnlyComposable
    fun meaning(color: Color): Color = if (LocalQuietColour.current) LocalAbColors.current.muted else color
}
