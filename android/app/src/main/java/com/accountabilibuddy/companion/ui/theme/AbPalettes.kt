package com.accountabilibuddy.companion.ui.theme

import androidx.compose.ui.graphics.Color

/** The six palettes (three themes, light and dark), copied from the PC's theme dictionaries (ADR 49). */
object AbPalettes {
    fun of(theme: ThemeName, dark: Boolean): AbColors = when (theme) {
        ThemeName.Instrument -> if (dark) InstrumentDark else InstrumentLight
        ThemeName.NightDesk -> if (dark) NightDeskDark else NightDeskLight
        ThemeName.Paper -> if (dark) PaperDark else PaperLight
    }

    private fun c(hex: Long) = Color(0xFF000000 or hex)

    val InstrumentLight = AbColors(
        isDark = false,
        bg = c(0xF3F4F5), surface = c(0xFFFFFF), surface2 = c(0xE6E9EB), line = c(0xDDE1E4),
        ink = c(0x15181B), muted = c(0x5B636B), faint = c(0x8E969E),
        accent = c(0x0F766E), onAccent = c(0xFFFFFF), accentSoft = c(0xDCEFEC),
        infoBg = c(0xE4EBF0), infoInk = c(0x2C3E50),
        sleep = c(0x5865A8), waiting = c(0xB7791F), met = c(0x0F766E), drift = c(0xC2412D),
        plan = c(0xC9CED3), upcoming = c(0xB4BBC2),
        amberInk = c(0x8A5300), amberFill = c(0xFCF1D8), amberLine = c(0xEBCB7E),
    )

    val InstrumentDark = AbColors(
        isDark = true,
        bg = c(0x111315), surface = c(0x181B1E), surface2 = c(0x202428), line = c(0x2C3136),
        ink = c(0xE8EAEC), muted = c(0x8E969E), faint = c(0x5A626A),
        accent = c(0x5EEAD4), onAccent = c(0x06201D), accentSoft = c(0x17312E),
        infoBg = c(0x1A2530), infoInk = c(0xB4C6D6),
        sleep = c(0x8E9BD6), waiting = c(0xE8B04B), met = c(0x5EEAD4), drift = c(0xF0705A),
        plan = c(0x3A4148), upcoming = c(0x444C54),
        amberInk = c(0xF2C46D), amberFill = c(0x3A2C12), amberLine = c(0x6B5124),
    )

    val NightDeskLight = AbColors(
        isDark = false,
        bg = c(0xEEF1F5), surface = c(0xFFFFFF), surface2 = c(0xE3E7EE), line = c(0xE3E7EE),
        ink = c(0x1A2233), muted = c(0x5E6B80), faint = c(0x8A96A8),
        accent = c(0x8C6D45), onAccent = c(0xFFFFFF), accentSoft = c(0xEFE9DF),
        infoBg = c(0xE8EBF3), infoInk = c(0x2E3757),
        sleep = c(0x5865A8), waiting = c(0x5865A8), met = c(0x3F7A5A), drift = c(0xB5523F),
        plan = c(0xCDD4DF), upcoming = c(0xB9C2D0),
        amberInk = c(0x8A5300), amberFill = c(0xFBF0D9), amberLine = c(0xE6C887),
    )

    val NightDeskDark = AbColors(
        isDark = true,
        bg = c(0x10141C), surface = c(0x171C27), surface2 = c(0x1F2532), line = c(0x283040),
        ink = c(0xDCE1EA), muted = c(0x8C95A8), faint = c(0x5A6378),
        accent = c(0xD2B98F), onAccent = c(0x241B0E), accentSoft = c(0x2A2822),
        infoBg = c(0x1B2134), infoInk = c(0xB7C0DE),
        sleep = c(0xA7B2D6), waiting = c(0xA7B2D6), met = c(0x8FBFA6), drift = c(0xD99A8E),
        plan = c(0x343C52), upcoming = c(0x46506A),
        amberInk = c(0xE9C27A), amberFill = c(0x33280F), amberLine = c(0x5E4A22),
    )

    val PaperLight = AbColors(
        isDark = false,
        bg = c(0xF5F2EB), surface = c(0xFBFAF6), surface2 = c(0xECE7DC), line = c(0xDDD7CA),
        ink = c(0x1F1D1A), muted = c(0x6B655B), faint = c(0x8C8578),
        accent = c(0x4E6B3B), onAccent = c(0xFFFFFF), accentSoft = c(0xE7ECDF),
        infoBg = c(0xE9ECE2), infoInk = c(0x34452A),
        sleep = c(0x6E6A9E), waiting = c(0xA8741D), met = c(0x3F7A5A), drift = c(0xB5523F),
        plan = c(0xCFC8B9), upcoming = c(0xBDB6A6),
        amberInk = c(0x7F4F00), amberFill = c(0xF8EBCB), amberLine = c(0xE0C27A),
    )

    val PaperDark = AbColors(
        isDark = true,
        bg = c(0x1A1917), surface = c(0x22211E), surface2 = c(0x2C2A26), line = c(0x34312C),
        ink = c(0xEDE8DE), muted = c(0xA39C8F), faint = c(0x6F695F),
        accent = c(0xB3C79A), onAccent = c(0x1C2614), accentSoft = c(0x2B3124),
        infoBg = c(0x292D24), infoInk = c(0xC9D4B8),
        sleep = c(0xA9A5D6), waiting = c(0xDDB25E), met = c(0x8DBFA0), drift = c(0xD99A8E),
        plan = c(0x4A463F), upcoming = c(0x5A554D),
        amberInk = c(0xE3B862), amberFill = c(0x36291A), amberLine = c(0x6A5530),
    )
}
