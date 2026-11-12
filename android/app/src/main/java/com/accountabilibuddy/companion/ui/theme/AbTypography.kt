package com.accountabilibuddy.companion.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp

/**
 * A theme's type: a display font for headlines and a numeral font for counts and timers (all timers use tabular
 * numerals, so the digits don't jitter as they change). Sizes follow the design's scale and are the same in every
 * theme; a theme changes only the fonts. Body text uses the system font.
 */
class AbTypography(
    private val displayFamily: FontFamily,
    private val displayWeight: FontWeight,
    private val displaySpacing: TextUnit,
    private val numeralFamily: FontFamily,
    private val numeralWeight: FontWeight,
) {
    private fun display(size: Int) = TextStyle(
        fontFamily = displayFamily, fontWeight = displayWeight, fontSize = size.sp, lineHeight = (size * 1.1f).sp, letterSpacing = displaySpacing,
    )

    private fun numeral(size: Int) = TextStyle(
        fontFamily = numeralFamily, fontWeight = numeralWeight, fontSize = size.sp, lineHeight = (size * 1.1f).sp,
        fontFeatureSettings = TABULAR,
    )

    /** A big count: "1h 10m", "7". */
    val count: TextStyle = numeral(54)

    /** The running timer on Today (the board's 72 px). */
    val timer: TextStyle = numeral(72)

    /** The biggest timers (a calm full-screen timer). */
    val timerHuge: TextStyle = numeral(96)

    /** Smaller timers and numbers inside a card. */
    val timerSmall: TextStyle = numeral(22)

    /** Page title (the boards' 30 px). */
    val title: TextStyle = display(30)

    /** Headline inside a page. */
    val head: TextStyle = display(24)

    /** Card or section heading. */
    val section: TextStyle = display(20)

    val body: TextStyle = TextStyle(fontSize = 16.sp, lineHeight = 23.sp)
    val bodyMedium: TextStyle = TextStyle(fontSize = 15.sp, lineHeight = 22.sp)
    val small: TextStyle = TextStyle(fontSize = 13.sp, lineHeight = 18.sp)
    val micro: TextStyle = TextStyle(fontSize = 12.sp, lineHeight = 16.sp, letterSpacing = 0.04.em)

    /** The theme's fonts on the stock Material 3 type scale, so screens not redone yet follow the theme (sizes unchanged). */
    fun material(): Typography {
        val base = Typography()
        fun TextStyle.themed() = copy(fontFamily = displayFamily, fontWeight = displayWeight, letterSpacing = displaySpacing)
        return base.copy(
            displayLarge = base.displayLarge.themed(),
            displayMedium = base.displayMedium.themed(),
            displaySmall = base.displaySmall.themed(),
            headlineLarge = base.headlineLarge.themed(),
            headlineMedium = base.headlineMedium.themed(),
            headlineSmall = base.headlineSmall.themed(),
            titleLarge = base.titleLarge.themed(),
        )
    }

    companion object {
        private const val TABULAR = "tnum"

        fun of(theme: ThemeName): AbTypography = when (theme) {
            ThemeName.Instrument -> AbTypography(
                displayFamily = FontFamily.Default, displayWeight = FontWeight.SemiBold, displaySpacing = 0.em,
                numeralFamily = AbFonts.IbmPlexMono, numeralWeight = FontWeight.Medium,
            )
            ThemeName.NightDesk -> AbTypography(
                displayFamily = AbFonts.Manrope, displayWeight = FontWeight.SemiBold, displaySpacing = (-0.02).em,
                numeralFamily = AbFonts.Manrope, numeralWeight = FontWeight.SemiBold,
            )
            ThemeName.Paper -> AbTypography(
                displayFamily = AbFonts.Newsreader, displayWeight = FontWeight.Medium, displaySpacing = 0.em,
                numeralFamily = AbFonts.Newsreader, numeralWeight = FontWeight.Normal,
            )
        }
    }
}
