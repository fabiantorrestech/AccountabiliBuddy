package com.accountabilibuddy.shared.tasks

import java.util.Locale

/**
 * Label colours (ADR 76; the PC's TaskLabelColors): the 12-colour set new labels start on, and the forms a colour may take:
 * "#RRGGBB", "#RRGGBBAA" or "rgba(r,g,b,a)". The old sync fields only take "#RRGGBB", so they get [opaque].
 */
object TaskLabelColors {
    /** The 12 colours a new label starts on, the first unused one first. */
    val SET: List<String> = listOf(
        "#2F6BEA", "#14998C", "#EA6A12", "#E0322C", "#7C4DFF", "#22A350",
        "#C2410C", "#0E7490", "#A21CAF", "#4D7C0F", "#B45309", "#BE123C",
    )

    /** The lowest opacity a custom colour may have. */
    const val MIN_OPACITY = 0.2

    /** Whether the text is a colour in one of the accepted forms. */
    fun isValid(color: String?): Boolean = parse(color) != null

    /** The colour without its opacity, "#RRGGBB" in upper case; the set's first colour when it can't be read. */
    fun opaque(color: String?): String {
        val parsed = parse(color) ?: return SET[0]
        return String.format(Locale.ROOT, "#%02X%02X%02X", parsed.red, parsed.green, parsed.blue)
    }

    /** The colour's opacity, from 0 to 1; 1 when it can't be read. */
    fun opacityOf(color: String?): Double = parse(color)?.alpha ?: 1.0

    /** Whether two colours look the same once their opacity is ignored. */
    fun sameOpaque(first: String?, second: String?): Boolean = opaque(first) == opaque(second)

    /**
     * The colour with [opacity] (kept between [MIN_OPACITY] and 1): "#RRGGBB" when fully opaque, else "rgba(r,g,b,a)" with up to two
     * decimals (the PC's WithOpacity). The set's first colour when [color] can't be read.
     */
    fun withOpacity(color: String?, opacity: Double): String {
        val opaque = opaque(color)
        val alpha = Math.round(opacity.coerceIn(MIN_OPACITY, 1.0) * 100.0) / 100.0
        if (alpha >= 1.0) return opaque
        val parsed = parse(opaque)!!
        val text = java.text.DecimalFormat("0.##", java.text.DecimalFormatSymbols(Locale.ROOT)).format(alpha)
        return "rgba(${parsed.red},${parsed.green},${parsed.blue},$text)"
    }

    /**
     * What was typed as a colour ("#RRGGBB", "#RRGGBBAA", the same without the #, or "rgba(r,g,b,a)") in its stored form: "#RRGGBB" in
     * upper case when opaque, else "rgba(r,g,b,a)" ([withOpacity]; an opacity under [MIN_OPACITY] is raised to it). Null when it isn't a colour.
     */
    fun typed(text: String?): String? {
        var trimmed = (text ?: "").trim()
        if ((trimmed.length == 6 || trimmed.length == 8) && trimmed.all(::isAsciiHexDigit)) trimmed = "#$trimmed"
        val parsed = parse(trimmed) ?: return null
        return if (parsed.alpha >= 1.0 && !trimmed.startsWith("rgba", ignoreCase = true) && trimmed.length == 7) trimmed.uppercase(Locale.ROOT) else withOpacity(trimmed, parsed.alpha)
    }

    /** The first colour of [SET] no live label uses (by its opaque form); the first one when all are used. */
    fun firstUnused(labels: List<TaskLabel>): String {
        val used = labels.filter { it.isLive }.map { opaque(it.color) }.toSet()
        return SET.firstOrNull { it !in used } ?: SET[0]
    }

    private class Parsed(val red: Int, val green: Int, val blue: Int, val alpha: Double)

    private fun parse(color: String?): Parsed? {
        if (color.isNullOrBlank()) {
            return null
        }

        val text = color.trim()
        if (text.startsWith("#") && (text.length == 7 || text.length == 9) && text.drop(1).all(::isAsciiHexDigit)) {
            val alpha = if (text.length == 9) text.substring(7, 9).toInt(16) / 255.0 else 1.0
            return Parsed(text.substring(1, 3).toInt(16), text.substring(3, 5).toInt(16), text.substring(5, 7).toInt(16), alpha)
        }

        if (text.startsWith("rgba(", ignoreCase = true) && text.endsWith(")")) {
            val parts = text.substring(5, text.length - 1).split(",").map { it.trim() }
            if (parts.size == 4) {
                val red = byteOf(parts[0])
                val green = byteOf(parts[1])
                val blue = byteOf(parts[2])
                val alpha = parts[3].toDoubleOrNull()
                if (red != null && green != null && blue != null && alpha != null && alpha >= 0.0 && alpha <= 1.0) {
                    return Parsed(red, green, blue, alpha)
                }
            }
        }

        return null
    }

    private fun isAsciiHexDigit(char: Char): Boolean = char in '0'..'9' || char in 'a'..'f' || char in 'A'..'F'

    // A whole number from 0 to 255 in plain digits (the PC's byte.TryParse with NumberStyles.Integer also takes a leading "+").
    private fun byteOf(text: String): Int? {
        val digits = text.removePrefix("+")
        if (digits.isEmpty() || !digits.all { it in '0'..'9' }) {
            return null
        }

        return digits.toIntOrNull()?.takeIf { it in 0..255 }
    }
}
