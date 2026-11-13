package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlin.math.abs
import kotlin.math.max

/**
 * Compares the C# side's JSON with the Kotlin side's: the same names in the same order, the same texts, booleans and nulls,
 * and numbers equal within a small tolerance (C# prints doubles its own way, e.g. 245.85365853658539); a number C# writes whole
 * ("120") must be written whole by Kotlin too (not "120.0").
 */
object JsonParity {
    const val TOLERANCE = 1e-6

    /** Null when they agree, else the path of the first difference and both values. */
    fun difference(expected: JsonElement, actual: JsonElement, path: String = "$"): String? {
        when (expected) {
            is JsonObject -> {
                if (actual !is JsonObject) return "$path: C# object vs kotlin $actual"
                if (expected.keys.toList() != actual.keys.toList()) return "$path: C# keys ${expected.keys} vs kotlin ${actual.keys}"
                for ((key, value) in expected) {
                    difference(value, actual.getValue(key), "$path.$key")?.let { return it }
                }

                return null
            }

            is JsonArray -> {
                if (actual !is JsonArray) return "$path: C# array vs kotlin $actual"
                if (expected.size != actual.size) return "$path: C# ${expected.size} items vs kotlin ${actual.size}"
                expected.forEachIndexed { index, value -> difference(value, actual[index], "$path[$index]")?.let { return it } }
                return null
            }

            is JsonNull -> return if (actual is JsonNull) null else "$path: C# null vs kotlin $actual"

            is JsonPrimitive -> {
                if (actual !is JsonPrimitive || actual is JsonNull) return "$path: C# $expected vs kotlin $actual"
                if (expected.isString || actual.isString) {
                    // A time the phone writes carries a mark for the text writer (GoalJson.timeValue); the tree compares without it.
                    val same = expected.isString == actual.isString && GoalJson.unmark(expected.content) == GoalJson.unmark(actual.content)
                    return if (same) null else "$path: C# $expected vs kotlin $actual"
                }

                val a = expected.content.toDoubleOrNull()
                val b = actual.content.toDoubleOrNull()
                if (a != null && b != null && !hasFraction(expected.content) && hasFraction(actual.content)) {
                    return "$path: C# writes ${expected.content} without a fraction, kotlin ${actual.content}"
                }

                if (a != null && b != null) {
                    return if (abs(a - b) <= TOLERANCE * max(1.0, max(abs(a), abs(b)))) null else "$path: C# $a vs kotlin $b"
                }

                return if (expected.content == actual.content) null else "$path: C# $expected vs kotlin $actual"
            }
        }
    }

    // A point or an exponent: a whole number the PC writes as "120" must not come out as "120.0".
    private fun hasFraction(number: String): Boolean = number.any { it == '.' || it == 'e' || it == 'E' }
}
