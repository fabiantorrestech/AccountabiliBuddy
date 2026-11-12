package com.accountabilibuddy.companion.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp

/**
 * One theme's colours in one variant (light or dark): the same names and values as the PC's `Ab.*` keys (ADR 49), so a
 * colour fix is made in both places. Accent shows only for progress and the one main button; active nav, numbering and
 * selected chips use ink on a tonal surface.
 */
class AbColors(
    val isDark: Boolean,
    val bg: Color,
    val surface: Color,
    val surface2: Color,
    val line: Color,
    val ink: Color,
    val muted: Color,
    val faint: Color,
    val accent: Color,
    val onAccent: Color,
    val accentSoft: Color,
    val infoBg: Color,
    val infoInk: Color,
    val sleep: Color,
    val waiting: Color,
    val met: Color,
    val drift: Color,
    val plan: Color,
    val upcoming: Color,
    /** Round 2 (brief §3.8): late or behind pills ("Behind", "Overdue 3 d"): ink on fill with a line, the PC's `Ab.Amber*`. */
    val amberInk: Color,
    val amberFill: Color,
    val amberLine: Color,
) {
    /**
     * The five heatmap steps, level 0 (empty, the surface2 tone) to level 4 (accent). The steps between mix the two, the
     * way the PC's heat ramp does, so every theme gets a ramp from its own colours.
     */
    val heatmap: List<Color> = HEAT_MIX.map { share -> lerp(surface2, accent, share) }

    private companion object {
        val HEAT_MIX = listOf(0f, 0.22f, 0.45f, 0.72f, 1f)
    }
}
