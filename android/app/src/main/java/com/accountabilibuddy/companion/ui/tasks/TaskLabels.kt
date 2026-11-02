package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.ui.graphics.Color

// "#RRGGBB" as the PC sends it; anything else is null, so the caller falls back to the theme's colour.
internal fun parseCategoryColor(hex: String?): Color? {
    if (hex == null || hex.length != 7 || hex[0] != '#') return null
    val rgb = hex.substring(1).toLongOrNull(16) ?: return null
    return Color(0xFF000000 or rgb)
}

