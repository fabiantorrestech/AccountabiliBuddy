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

    /** Whether the text is a colour in one of the accepted forms. */
    fun isValid(color: String?): Boolean = parse(color) != null

    /** The colour without its opacity, "#RRGGBB" in upper case; the set's first colour when it can't be read. */
    fun opaque(color: String?): String {
        val parsed = parse(color) ?: return SET[0]
        return String.format(Locale.ROOT, "#%02X%02X%02X", parsed.red, parsed.green, parsed.blue)
    }

    /** The colour's opacity, from 0 to 1; 1 when it can't be read. */
    fun opacityOf(color: String?): Double = parse(color)?.alpha ?: 1.0

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
