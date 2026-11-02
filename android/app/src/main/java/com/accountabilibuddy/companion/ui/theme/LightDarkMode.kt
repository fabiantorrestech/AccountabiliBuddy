package com.accountabilibuddy.companion.ui.theme

/** How the app picks light or dark. The numbers are stored and match the PC's, so keep them. */
enum class LightDarkMode(val code: Int, val label: String) {
    /** Follows the phone's own setting. */
    System(0, "System"),
    Light(1, "Light"),
    Dark(2, "Dark"),

    /** Dark from one time of day to another. */
    SetTime(3, "At a set time"),

    /** Dark from sunset to sunrise at a place the user typed or picked. */
    Sunset(4, "After sunset");

    companion object {
        fun fromCode(code: Int): LightDarkMode = entries.firstOrNull { it.code == code } ?: System
    }
}
