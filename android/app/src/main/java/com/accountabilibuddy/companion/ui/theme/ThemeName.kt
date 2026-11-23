package com.accountabilibuddy.companion.ui.theme

/**
 * The themes (ADR 49): the PC's three, with the same names, and E-Ink, which only the phone has (owner, AB-Improvements #29).
 * Each has a light and a dark version. The choice stays on this phone (never synced).
 */
enum class ThemeName(val label: String) {
    Instrument("Instrument"),
    NightDesk("Night Desk"),
    Paper("Paper"),

    /** High contrast for e-ink screens: black on white (white on black in dark), no tinted fills, firm edges. Phone only. */
    EInk("E-Ink"),
}
