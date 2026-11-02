package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.ui.graphics.Color

// "#RRGGBB" as the PC sends it; anything else is null, so the caller falls back to the theme's colour.
internal fun parseCategoryColor(hex: String?): Color? {
    if (hex == null || hex.length != 7 || hex[0] != '#') return null
    val rgb = hex.substring(1).toLongOrNull(16) ?: return null
    return Color(0xFF000000 or rgb)
}

/** 1 = "a.", 26 = "z.", 27 = "aa.", 28 = "ab." */
internal fun subtaskLetter(place: Int): String {
    var rest = place
    val letters = StringBuilder()
    while (rest > 0) {
        rest--
        letters.insert(0, 'a' + rest % 26)
        rest /= 26
    }
    return "$letters."
}
