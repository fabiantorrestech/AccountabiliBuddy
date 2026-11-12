package com.accountabilibuddy.companion.ui.sheets

/**
 * The two heights of a round 2 phone sheet (brief §3.7): about half the screen (446 dp on an 892 dp screen) for menus and More
 * today, about 80% (714 dp) for pickers and editors. [fraction] is the share of the window's height.
 */
enum class AbSheetHeight(val fraction: Float) {
    Half(0.5f),
    Tall(0.8f),
}
