package com.accountabilibuddy.companion.ui.theme

import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import com.accountabilibuddy.companion.R

/**
 * The bundled fonts (res/font, SIL Open Font License; the licence texts are in assets/licenses and the credit is in
 * About). The same files as the PC's (ADR 49). Body text stays on the system font.
 */
object AbFonts {
    val IbmPlexMono: FontFamily = FontFamily(
        Font(R.font.ibm_plex_mono_regular, FontWeight.Normal),
        Font(R.font.ibm_plex_mono_medium, FontWeight.Medium),
    )

    val Manrope: FontFamily = FontFamily(
        Font(R.font.manrope_medium, FontWeight.Medium),
        Font(R.font.manrope_semibold, FontWeight.SemiBold),
        Font(R.font.manrope_bold, FontWeight.Bold),
    )

    val Newsreader: FontFamily = FontFamily(
        Font(R.font.newsreader_regular, FontWeight.Normal),
        Font(R.font.newsreader_medium, FontWeight.Medium),
    )
}
