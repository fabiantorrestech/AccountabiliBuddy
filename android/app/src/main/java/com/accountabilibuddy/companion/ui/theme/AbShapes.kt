package com.accountabilibuddy.companion.ui.theme

import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * A theme's corner radii and card edge. Night Desk is soft with no card edge (tone only); Instrument and Paper are
 * tighter with a 1 dp hairline. [large] is a card, [medium] an inset row or panel, [small] a chip or small control.
 */
class AbShapes(
    val small: Dp,
    val medium: Dp,
    val large: Dp,
    val cardEdge: Dp,
) {
    val smallShape: Shape = RoundedCornerShape(small)
    val mediumShape: Shape = RoundedCornerShape(medium)
    val cardShape: Shape = RoundedCornerShape(large)
    val pill: Shape = CircleShape

    companion object {
        fun of(theme: ThemeName): AbShapes = when (theme) {
            ThemeName.NightDesk -> AbShapes(small = 14.dp, medium = 20.dp, large = 28.dp, cardEdge = 0.dp)
            ThemeName.Instrument -> AbShapes(small = 8.dp, medium = 10.dp, large = 14.dp, cardEdge = 1.dp)
            ThemeName.Paper -> AbShapes(small = 10.dp, medium = 14.dp, large = 18.dp, cardEdge = 1.dp)
        }
    }
}
