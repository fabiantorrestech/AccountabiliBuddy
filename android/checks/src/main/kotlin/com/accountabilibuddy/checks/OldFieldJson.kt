package com.accountabilibuddy.checks

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.OffsetDateTime

/**
 * Compares the old task fields as the phone writes them with the PC's, by what the PC reads from them rather than byte for byte:
 * the phone has always written a position as "1.0" where the PC writes "1", a time in UTC, and an empty `subtasks` list inside each
 * step (which the PC ignores), and leaves out a step's computed "isDone" (which the PC writes and never reads). Everything else must be the same: the names, their order, texts, numbers and moments.
 */
object OldFieldJson {
    /** Null when the PC reads the same from both, else the path of the first difference. */
    fun difference(expected: JsonElement, actual: JsonElement, path: String = "$"): String? {
        when (expected) {
            is JsonObject -> {
                if (actual !is JsonObject) return "$path: C# object vs kotlin $actual"
                val keys = actual.keys.filter { key -> key in expected || !(key == "subtasks" && (actual[key] as? JsonArray)?.isEmpty() == true) }
                // The PC also writes a step's computed "isDone", which nobody reads.
                val wanted = expected.filterKeys { it != "isDone" || it in actual }
                if (wanted.keys.toList() != keys) return "$path: C# keys ${wanted.keys} vs kotlin $keys"
                for ((key, value) in wanted) {
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
                if (actual !is JsonPrimitive || actual is JsonNull || expected.isString != actual.isString) return "$path: C# $expected vs kotlin $actual"
                if (expected.content == actual.content) return null
                val same = if (expected.isString) {
                    val a = runCatching { OffsetDateTime.parse(expected.content).toInstant() }.getOrNull()
                    val b = runCatching { OffsetDateTime.parse(actual.content).toInstant() }.getOrNull()
                    a != null && a == b
                } else {
                    val a = expected.content.toDoubleOrNull()
                    a != null && a == actual.content.toDoubleOrNull()
                }

                return if (same) null else "$path: C# $expected vs kotlin $actual"
            }
        }
    }
}
