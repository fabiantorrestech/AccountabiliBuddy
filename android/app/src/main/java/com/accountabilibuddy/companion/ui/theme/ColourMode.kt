package com.accountabilibuddy.companion.ui.theme

/** How much colour the app uses. The numbers match the PC's. */
enum class ColourMode(val code: Int, val label: String) {
    /** Colour for progress and the one main button; everything else stays ink. */
    Quiet(0, "Quiet"),
    Standard(1, "Standard");

    companion object {
        fun fromCode(code: Int): ColourMode = entries.firstOrNull { it.code == code } ?: Quiet
    }
}
